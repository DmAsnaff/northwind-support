namespace SupportDesk.Application.Exceptions;

/// <summary>
/// Raised when credentials or tokens are rejected. Mapped to <c>401 Unauthorized</c>.
/// </summary>
public sealed class UnauthorizedException(string message) : Exception(message);

