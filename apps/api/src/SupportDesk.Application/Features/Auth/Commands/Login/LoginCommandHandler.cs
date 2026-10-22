using Microsoft.Extensions.Logging;
using SupportDesk.Application.Abstractions;
using SupportDesk.Application.Contracts.Auth;
using SupportDesk.Application.Exceptions;
using SupportDesk.Domain.Repositories;

namespace SupportDesk.Application.Features.Auth.Commands.Login;

/// <summary>
/// Authenticates a support agent by email and password and issues a signed JWT.
/// </summary>
public sealed class LoginCommandHandler(
    IAgentRepository agents,
    IPasswordHasher passwordHasher,
    ITokenService tokenService,
    IClock clock,
    ILogger<LoginCommandHandler> logger)
{
    public const string GenericInvalidCredentialsMessage = "Invalid email or password.";

    /// <exception cref="UnauthorizedException">
    /// Email not found, password invalid, or agent is inactive. Always returns the same
    /// generic message so the endpoint never leaks account existence or status.
    /// </exception>
    public async Task<LoginResponseDto> HandleAsync(LoginRequest request, CancellationToken ct)
    {
        var normalizedEmail = request.Email?.Trim() ?? string.Empty;
        var password = request.Password ?? string.Empty;

        var agent = await agents.GetByEmailAsync(normalizedEmail, ct);

        if (agent is null || string.IsNullOrEmpty(agent.PasswordHash))
        {
            logger.LogInformation("Rejected sign-in attempt for unknown or unconfigured email.");
            throw new UnauthorizedException(GenericInvalidCredentialsMessage);
        }

        if (!passwordHasher.Verify(password, agent.PasswordHash) || !agent.IsActive)
        {
            logger.LogInformation("Rejected sign-in attempt for agent {AgentId}.", agent.Id);
            throw new UnauthorizedException(GenericInvalidCredentialsMessage);
        }

        var issued = tokenService.IssueToken(agent, clock.UtcNow);

        logger.LogInformation("Agent {AgentId} ({Email}) signed in.", agent.Id, agent.Email);

        return new LoginResponseDto(
            issued.Token,
            issued.ExpiresAtUtc,
            new AuthenticatedUserDto(agent.Id, agent.FullName, agent.Email));
    }
}

