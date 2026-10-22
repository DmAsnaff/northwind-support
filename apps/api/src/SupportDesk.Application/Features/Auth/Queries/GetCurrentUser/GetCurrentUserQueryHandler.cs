using SupportDesk.Application.Contracts.Auth;
using SupportDesk.Application.Exceptions;
using SupportDesk.Domain.Repositories;

namespace SupportDesk.Application.Features.Auth.Queries.GetCurrentUser;

/// <summary>
/// Returns the currently authenticated support agent.
/// </summary>
public sealed class GetCurrentUserQueryHandler(IAgentRepository agents)
{
    public async Task<AuthenticatedUserDto> HandleAsync(int? agentId, CancellationToken ct)
    {
        if (agentId is null)
        {
            throw new UnauthorizedException("Authentication is required.");
        }

        var agent = await agents.GetByIdAsync(agentId.Value, ct);
        if (agent is null || !agent.IsActive)
        {
            throw new UnauthorizedException("The authenticated agent account is no longer active.");
        }

        return new AuthenticatedUserDto(agent.Id, agent.FullName, agent.Email);
    }
}

