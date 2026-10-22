using SupportDesk.Domain.Aggregates.Agents;
using SupportDesk.Domain.Aggregates.Categories;

namespace SupportDesk.Domain.Aggregates.Tickets;

/// <summary>
/// Pairs an <see cref="Agent"/> with their current count of open tickets.
/// </summary>
public sealed record AgentWorkload(Agent Agent, int OpenTicketCount);

/// <summary>
/// Result of evaluating priority or agent assignment during triage or escalation.
/// </summary>
public sealed record PriorityDecision(TicketPriority Priority, string Reason);

/// <summary>
/// Result of selecting an agent during triage or escalation.
/// </summary>
public sealed record AssignmentDecision(Agent? Agent, string Reason);

/// <summary>
/// Domain rules for ticket priority resolution and agent assignment (BR-1, BR-4, BR-5, BR-7).
/// Shared between initial ticket creation and ticket escalation.
/// </summary>
public static class TicketTriagePolicy
{
    /// <summary>
    /// Determines the effective priority for a newly created ticket (BR-1).
    /// </summary>
    public static PriorityDecision ResolvePriority(TicketPriority? requestedPriority, Category category)
    {
        ArgumentNullException.ThrowIfNull(category);

        if (category.ForcesCriticalPriority)
        {
            return new PriorityDecision(
                TicketPriority.Critical,
                $"Priority forced to Critical because category '{category.Name}' requires Critical priority.");
        }

        if (requestedPriority.HasValue)
        {
            return new PriorityDecision(
                requestedPriority.Value,
                $"Priority set to {requestedPriority.Value} as requested.");
        }

        return new PriorityDecision(
            TicketPriority.Medium,
            "Priority defaulted to Medium because no priority was requested.");
    }

    /// <summary>
    /// Checks whether a single agent is eligible to take work in the given category (BR-4, BR-5).
    /// </summary>
    public static bool IsAgentEligible(Agent agent, int openTicketCount, Category category)
    {
        if (!agent.IsActive)
        {
            return false;
        }

        if (category.RequiresSpecialist && !agent.HasSpecialization(category.Id))
        {
            return false;
        }

        return openTicketCount < agent.MaxOpenTickets;
    }

    /// <summary>
    /// Selects the eligible agent with the fewest open tickets, breaking ties deterministically
    /// by lowest <see cref="Entity.Id"/> (BR-4, BR-5).
    /// </summary>
    public static AssignmentDecision SelectAgent(
        Category category,
        IReadOnlyCollection<AgentWorkload> candidates)
    {
        ArgumentNullException.ThrowIfNull(category);
        ArgumentNullException.ThrowIfNull(candidates);

        var eligible = candidates
            .Where(c => IsAgentEligible(c.Agent, c.OpenTicketCount, category))
            .OrderBy(c => c.OpenTicketCount)
            .ThenBy(c => c.Agent.Id)
            .ToList();

        if (eligible.Count > 0)
        {
            var chosen = eligible[0];
            var specialistNote = category.RequiresSpecialist
                ? $", specialist in {category.Name}"
                : string.Empty;

            return new AssignmentDecision(
                chosen.Agent,
                $"Assigned to {chosen.Agent.FullName} ({chosen.OpenTicketCount}/{chosen.Agent.MaxOpenTickets} open tickets{specialistNote}).");
        }

        if (category.RequiresSpecialist)
        {
            var activeSpecialists = candidates
                .Where(c => c.Agent.IsActive && c.Agent.HasSpecialization(category.Id))
                .ToList();

            if (activeSpecialists.Count == 0)
            {
                return new AssignmentDecision(
                    null,
                    $"Unassigned because category '{category.Name}' requires a specialist and no active specialist is available.");
            }

            return new AssignmentDecision(
                null,
                $"Unassigned because all active specialists for '{category.Name}' have reached their maximum open ticket limit.");
        }

        return new AssignmentDecision(
            null,
            "Unassigned because no active agent has capacity below their maximum open ticket limit.");
    }

    /// <summary>
    /// Re-evaluates ticket ownership on escalation: keeps the current agent if still eligible,
    /// otherwise selects a new agent per BR-5 (BR-7).
    /// </summary>
    public static AssignmentDecision ReevaluateAgentForEscalation(
        int? currentAgentId,
        Category category,
        IReadOnlyCollection<AgentWorkload> candidates)
    {
        if (currentAgentId.HasValue)
        {
            var current = candidates.FirstOrDefault(c => c.Agent.Id == currentAgentId.Value);
            if (current is not null && IsAgentEligible(current.Agent, current.OpenTicketCount, category))
            {
                return new AssignmentDecision(
                    current.Agent,
                    $"Retained current owner {current.Agent.FullName} who remains eligible.");
            }
        }

        return SelectAgent(category, candidates);
    }
}

