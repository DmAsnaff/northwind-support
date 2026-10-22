using System.Net.Mime;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SupportDesk.Application.Abstractions;
using SupportDesk.Infrastructure.Services;

namespace SupportDesk.Presentation.Authentication;

/// <summary>
/// Validates <c>Authorization: Bearer &lt;jwt&gt;</c> headers using <see cref="JwtTokenService"/>
/// and writes RFC 7807 <see cref="ProblemDetails"/> on 401 challenges.
/// </summary>
public sealed class BearerTokenAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    JwtTokenService tokenService,
    IClock clock) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Bearer";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("Authorization", out var headerValues))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var header = headerValues.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var token = header["Bearer ".Length..].Trim();
        if (string.IsNullOrEmpty(token))
        {
            return Task.FromResult(AuthenticateResult.Fail("Bearer token is empty."));
        }

        var principal = tokenService.ValidateToken(token, clock.UtcNow);
        if (principal is null)
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid or expired bearer token."));
        }

        var ticket = new AuthenticationTicket(principal, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.ContentType = MediaTypeNames.Application.Json;

        var problem = new ProblemDetails
        {
            Title = "Unauthorized",
            Detail = "A valid bearer token is required to access this endpoint.",
            Status = StatusCodes.Status401Unauthorized
        };
        problem.Extensions["traceId"] = Context.TraceIdentifier;

        await Response.WriteAsJsonAsync(problem);
    }
}

