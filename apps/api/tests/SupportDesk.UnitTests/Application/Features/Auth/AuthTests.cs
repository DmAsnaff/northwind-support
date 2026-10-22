using System.Security.Claims;
using Microsoft.Extensions.Logging.Abstractions;
using SupportDesk.Application.Contracts.Auth;
using SupportDesk.Application.Contracts.Tickets;
using SupportDesk.Application.Exceptions;
using SupportDesk.Application.Features.Auth.Commands.Login;
using SupportDesk.Application.Features.Auth.Queries.GetCurrentUser;
using SupportDesk.Domain.Aggregates.Agents;
using SupportDesk.Domain.Common;
using SupportDesk.Domain.Repositories;
using SupportDesk.Infrastructure.Services;
using SupportDesk.UnitTests.Application.Features.Tickets.Commands;
using SupportDesk.UnitTests.TestDoubles;

namespace SupportDesk.UnitTests.Application.Features.Auth;

public class AuthTests
{
    private readonly Pbkdf2PasswordHasher _hasher = new();
    private readonly FixedClock _clock = new();
    private readonly JwtTokenService _tokenService = new(new JwtSettings
    {
        SigningKey = "test-only-jwt-signing-key-with-at-least-32-bytes!",
        Issuer = "NorthwindSupportDesk",
        Audience = "NorthwindSupportDesk.Client",
        ExpiryMinutes = 60
    });

    private Agent CreateSeededAgent(bool isActive = true)
    {
        var agent = new Agent("Alex Turner", "alex.turner@northwind-support.example", 10, _clock.UtcNow);
        typeof(Entity).GetProperty(nameof(Entity.Id))!.SetValue(agent, 1);
        agent.SetPasswordHash(_hasher.Hash("Dev_Support_Pass1!"));

        if (!isActive)
        {
            agent.Deactivate();
        }

        return agent;
    }

    [Fact]
    public void PasswordHasher_VerifiesCorrectPasswordAndRejectsWrongPassword()
    {
        var hash = _hasher.Hash("Dev_Support_Pass1!");

        Assert.True(_hasher.Verify("Dev_Support_Pass1!", hash));
        Assert.False(_hasher.Verify("Wrong_Password!", hash));
        Assert.False(_hasher.Verify("Dev_Support_Pass1!", "invalid-hash"));
    }

    [Fact]
    public async Task Login_WithWrongPasswordOrUnknownEmail_ThrowsIdenticalUnauthorizedMessageAndIssuesNoToken()
    {
        var agentRepo = new Mock<IAgentRepository>();
        var agent = CreateSeededAgent();
        agentRepo.Setup(r => r.GetByEmailAsync(agent.Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(agent);

        var handler = new LoginCommandHandler(
            agentRepo.Object,
            _hasher,
            _tokenService,
            _clock,
            NullLogger<LoginCommandHandler>.Instance);

        var wrongPasswordEx = await Assert.ThrowsAsync<UnauthorizedException>(() =>
            handler.HandleAsync(new LoginRequest(agent.Email, "WrongPassword!"), CancellationToken.None));

        var wrongEmailEx = await Assert.ThrowsAsync<UnauthorizedException>(() =>
            handler.HandleAsync(new LoginRequest("unknown@northwind-support.example", "Dev_Support_Pass1!"), CancellationToken.None));

        Assert.Equal(LoginCommandHandler.GenericInvalidCredentialsMessage, wrongPasswordEx.Message);
        Assert.Equal(wrongEmailEx.Message, wrongPasswordEx.Message);
    }

    [Fact]
    public async Task Login_WhenAgentIsInactive_ThrowsUnauthorized()
    {
        var agentRepo = new Mock<IAgentRepository>();
        var inactive = CreateSeededAgent(isActive: false);
        agentRepo.Setup(r => r.GetByEmailAsync(inactive.Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(inactive);

        var handler = new LoginCommandHandler(
            agentRepo.Object,
            _hasher,
            _tokenService,
            _clock,
            NullLogger<LoginCommandHandler>.Instance);

        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            handler.HandleAsync(new LoginRequest(inactive.Email, "Dev_Support_Pass1!"), CancellationToken.None));
    }

    [Fact]
    public async Task Login_WithValidCredentials_IssuesValidJwtAndAuthenticatesCurrentUser()
    {
        var agentRepo = new Mock<IAgentRepository>();
        var agent = CreateSeededAgent();
        agentRepo.Setup(r => r.GetByEmailAsync(agent.Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(agent);
        agentRepo.Setup(r => r.GetByIdAsync(agent.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(agent);

        var loginHandler = new LoginCommandHandler(
            agentRepo.Object,
            _hasher,
            _tokenService,
            _clock,
            NullLogger<LoginCommandHandler>.Instance);

        var loginResponse = await loginHandler.HandleAsync(
            new LoginRequest(agent.Email, "Dev_Support_Pass1!"),
            CancellationToken.None);

        Assert.NotEmpty(loginResponse.Token);
        Assert.Equal(_clock.UtcNow.AddMinutes(60), loginResponse.ExpiresAtUtc);
        Assert.Equal("Alex Turner", loginResponse.User.FullName);

        // Token validates before expiry and fails once expired or missing
        var principal = _tokenService.ValidateToken(loginResponse.Token, _clock.UtcNow);
        Assert.NotNull(principal);
        Assert.Equal("1", principal!.FindFirst(ClaimTypes.NameIdentifier)?.Value);
        Assert.Equal("Alex Turner", principal.FindFirst(ClaimTypes.Name)?.Value);

        Assert.Null(_tokenService.ValidateToken(string.Empty, _clock.UtcNow));
        Assert.Null(_tokenService.ValidateToken(loginResponse.Token, _clock.UtcNow.AddMinutes(61)));

        var meHandler = new GetCurrentUserQueryHandler(agentRepo.Object);
        var currentUser = await meHandler.HandleAsync(1, CancellationToken.None);
        Assert.Equal(agent.Email, currentUser.Email);
        await Assert.ThrowsAsync<UnauthorizedException>(() => meHandler.HandleAsync(null, CancellationToken.None));
    }

    [Fact]
    public async Task EscalateTicket_UsesActorFromAuthenticatedTokenOverRequestBody()
    {
        var context = new TicketCommandTestContext();
        var ticket = context.ExistingTicket(new TicketBuilder().Build());

        var response = await context.EscalateTicket.HandleAsync(
            ticket.Id,
            new EscalateTicketRequest("Customer impact is expanding rapidly.", EscalatedBy: "Spoofed Body Actor"),
            CancellationToken.None,
            authenticatedActor: "Priya Nair");

        Assert.Equal("Priya Nair", response.Escalation.EscalatedBy);
    }
}

