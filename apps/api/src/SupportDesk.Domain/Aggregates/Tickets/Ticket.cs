using SupportDesk.Domain.Aggregates.Customers;
using SupportDesk.Domain.Common;
using SupportDesk.Domain.Exceptions;

namespace SupportDesk.Domain.Aggregates.Tickets;

/// <summary>
/// A support request raised by a customer. Every change to a ticket goes through one of its
/// methods, so its rules cannot be bypassed.
/// </summary>
/// <remarks>
/// The customer, the category and the assigned agent are other aggregates and are referenced
/// by id only.
/// </remarks>
public sealed class Ticket : AggregateRoot
{
    private readonly List<TicketEscalation> _escalations = [];

    private Ticket()
    {
        // For EF Core.
    }

    /// <summary>Human-friendly identifier shown to customers, e.g. TCK-0042.</summary>
    public string Reference { get; private set; } = string.Empty;

    public string Title { get; private set; } = string.Empty;

    public string Description { get; private set; } = string.Empty;

    public int CustomerId { get; private set; }

    public int CategoryId { get; private set; }

    /// <summary>Null while the ticket is waiting for an owner.</summary>
    public int? AssignedAgentId { get; private set; }

    public TicketPriority Priority { get; private set; }

    public TicketStatus Status { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    /// <summary>When the team has committed to responding by. Null when no SLA applies.</summary>
    public DateTime? DueAtUtc { get; private set; }

    public DateTime? ResolvedAtUtc { get; private set; }

    /// <summary>Immutable history of escalations performed on this ticket.</summary>
    public IReadOnlyCollection<TicketEscalation> Escalations => _escalations;

    /// <summary>True while the ticket still needs work from an agent.</summary>
    public bool IsOpen => Status is not (TicketStatus.Resolved or TicketStatus.Closed);

    /// <summary>True when the ticket is open and below <see cref="TicketPriority.Critical"/>.</summary>
    public bool CanEscalate => IsOpen && Priority < TicketPriority.Critical;

    /// <summary>
    /// Raises a new ticket for a customer.
    /// </summary>
    public static Ticket Raise(
        string reference,
        string title,
        string description,
        int customerId,
        int categoryId,
        TicketPriority priority,
        DateTime nowUtc,
        DateTime? dueAtUtc = null,
        int? assignedAgentId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        return new Ticket
        {
            Reference = reference,
            Title = title.Trim(),
            Description = description.Trim(),
            CustomerId = customerId,
            CategoryId = categoryId,
            AssignedAgentId = assignedAgentId,
            Priority = priority,
            Status = TicketStatus.New,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
            DueAtUtc = dueAtUtc
        };
    }

    /// <summary>
    /// Escalates the ticket one priority level, recomputes its due date from <paramref name="nowUtc"/>,
    /// updates its assigned agent, and records an immutable <see cref="TicketEscalation"/> history entry.
    /// </summary>
    /// <exception cref="BusinessRuleViolationException">
    /// The ticket is already Resolved/Closed or already at Critical priority.
    /// </exception>
    public TicketEscalation Escalate(
        SlaPolicy slaPolicy,
        CustomerTier customerTier,
        int? toAgentId,
        string reason,
        string escalatedBy,
        DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(slaPolicy);

        if (!IsOpen)
        {
            throw new BusinessRuleViolationException(
                $"Ticket {Reference} is {Status} and cannot be escalated because resolved or closed tickets cannot be escalated.");
        }

        if (Priority == TicketPriority.Critical)
        {
            throw new BusinessRuleViolationException(
                $"Ticket {Reference} is already at Critical priority and cannot be escalated further.");
        }

        var trimmedReason = reason?.Trim() ?? string.Empty;
        if (trimmedReason.Length is < 5 or > 500)
        {
            throw new ArgumentException("Escalation reason must be between 5 and 500 characters.", nameof(reason));
        }

        var trimmedActor = escalatedBy?.Trim() ?? string.Empty;
        if (trimmedActor.Length is < 1 or > 100)
        {
            throw new ArgumentException("EscalatedBy must be between 1 and 100 characters.", nameof(escalatedBy));
        }

        var fromPriority = Priority;
        var toPriority = Priority + 1;
        var fromAgentId = AssignedAgentId;
        var fromDueAtUtc = DueAtUtc;
        var toDueAtUtc = slaPolicy.CalculateDueAtUtc(nowUtc, toPriority, customerTier);

        Priority = toPriority;
        AssignedAgentId = toAgentId;
        DueAtUtc = toDueAtUtc;
        UpdatedAtUtc = nowUtc;

        var escalation = new TicketEscalation(
            Id,
            fromPriority,
            toPriority,
            fromAgentId,
            toAgentId,
            fromDueAtUtc,
            toDueAtUtc,
            trimmedReason,
            trimmedActor,
            nowUtc);

        _escalations.Add(escalation);
        return escalation;
    }

    /// <summary>
    /// Moves the ticket through its lifecycle, keeping the resolution timestamp in step.
    /// </summary>
    /// <exception cref="BusinessRuleViolationException">The ticket is closed.</exception>
    public void ChangeStatus(TicketStatus status, DateTime nowUtc)
    {
        if (Status == TicketStatus.Closed && status != TicketStatus.Closed)
        {
            throw new BusinessRuleViolationException(
                $"Ticket {Reference} is closed and cannot be moved to {status}.");
        }

        ResolvedAtUtc = status switch
        {
            TicketStatus.Resolved => nowUtc,
            TicketStatus.Closed => ResolvedAtUtc ?? nowUtc,
            _ => null
        };

        Status = status;
        UpdatedAtUtc = nowUtc;
    }

    /// <summary>
    /// Hands the ticket to an agent, or takes it back off them when <paramref name="agentId"/>
    /// is null. Whether the agent may take it is checked by the caller, which can see agents.
    /// </summary>
    public void AssignTo(int? agentId, DateTime nowUtc)
    {
        AssignedAgentId = agentId;
        UpdatedAtUtc = nowUtc;
    }
}
