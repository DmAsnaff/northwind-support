using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SupportDesk.Application.Contracts.Auth;
using SupportDesk.Application.Features.Auth.Commands.Login;
using SupportDesk.Application.Features.Auth.Queries.GetCurrentUser;

namespace SupportDesk.Presentation.Controllers;

[ApiController]
[Route("api/auth")]
[Produces("application/json")]
public sealed class AuthController : ControllerBase
{
    /// <summary>Authenticates a support agent and returns a signed JWT.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    public Task<LoginResponseDto> Login(
        LoginRequest request,
        [FromServices] LoginCommandHandler handler,
        CancellationToken ct) =>
        handler.HandleAsync(request, ct);

    /// <summary>Returns the currently signed-in support agent.</summary>
    [HttpGet("me")]
    public Task<AuthenticatedUserDto> Me(
        [FromServices] GetCurrentUserQueryHandler handler,
        CancellationToken ct)
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var agentId = int.TryParse(sub, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : (int?)null;

        return handler.HandleAsync(agentId, ct);
    }
}

