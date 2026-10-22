using SupportDesk.Domain.Aggregates.Tickets;

namespace SupportDesk.Application.Contracts.Tickets;

/// <summary>
/// Human-readable explanation of how priority, SLA window and assignment were decided on create.
/// </summary>
public sealed record TriageResultDto(
    string PriorityReason,
    string SlaReason,
    string AssignmentReason);

/// <summary>
/// One entry in a ticket's escalation history.
/// </summary>
public sealed class TicketEscalationDto
{
    public int Id { get; init; }

    public int TicketId { get; init; }

    public TicketPriority FromPriority { get; init; }

    public TicketPriority ToPriority { get; init; }

    public AgentSummaryDto? FromAgent { get; init; }

    public AgentSummaryDto? ToAgent { get; init; }

    public DateTime? FromDueAtUtc { get; init; }

    public DateTime ToDueAtUtc { get; init; }

    public string Reason { get; init; } = string.Empty;

    public string EscalatedBy { get; init; } = string.Empty;

    public DateTime EscalatedAtUtc { get; init; }
}

/// <summary>
/// Response returned by <c>POST /api/tickets/{id}/escalate</c>.
/// </summary>
public sealed record EscalateTicketResponseDto(
    TicketDetailDto Ticket,
    TicketEscalationDto Escalation);

