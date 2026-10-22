using SupportDesk.Domain.Aggregates.Agents;

namespace SupportDesk.Application.Abstractions;

public sealed record IssuedToken(string Token, DateTime ExpiresAtUtc);

/// <summary>
/// Issues signed JWT tokens for authenticated agents.
/// </summary>
public interface ITokenService
{
    IssuedToken IssueToken(Agent agent, DateTime nowUtc);
}

