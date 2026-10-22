using SupportDesk.Domain.Aggregates.Agents;
using SupportDesk.Domain.Aggregates.Tickets;

namespace SupportDesk.Domain.Repositories;

/// <summary>
/// Loads <see cref="Agent"/> aggregates.
/// </summary>
public interface IAgentRepository
{
    Task<Agent?> GetByIdAsync(int id, CancellationToken ct);

    /// <summary>
    /// Loads all agents (with their specializations) paired with their current open ticket counts.
    /// </summary>
    Task<IReadOnlyList<AgentWorkload>> GetWorkloadsAsync(CancellationToken ct);
}
