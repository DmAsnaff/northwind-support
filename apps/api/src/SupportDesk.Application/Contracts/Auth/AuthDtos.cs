namespace SupportDesk.Application.Contracts.Auth;

/// <summary>Body of POST /api/auth/login.</summary>
public sealed record LoginRequest(string Email, string Password);

/// <summary>Agent identity returned on login and from GET /api/auth/me.</summary>
public sealed record AuthenticatedUserDto(int Id, string FullName, string Email);

/// <summary>Response returned by POST /api/auth/login.</summary>
public sealed record LoginResponseDto(
    string Token,
    DateTime ExpiresAtUtc,
    AuthenticatedUserDto User);

