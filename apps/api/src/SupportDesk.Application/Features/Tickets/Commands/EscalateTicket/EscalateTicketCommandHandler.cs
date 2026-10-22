using Microsoft.Extensions.Logging;
using SupportDesk.Application.Abstractions;
using SupportDesk.Application.Contracts.Tickets;
using SupportDesk.Application.Exceptions;
using SupportDesk.Application.Features.Tickets.Queries.GetTicket;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.Domain.Repositories;

namespace SupportDesk.Application.Features.Tickets.Commands.EscalateTicket;

/// <summary>
/// Escalates a ticket one priority level, recomputes its SLA due date, re-evaluates its
/// assigned agent, and writes an immutable escalation history entry.
/// </summary>
public sealed class EscalateTicketCommandHandler(
    ITicketRepository tickets,
    ICustomerRepository customers,
    ICategoryRepository categories,
    IAgentRepository agents,
    SlaPolicy slaPolicy,
    IUnitOfWork unitOfWork,
    IClock clock,
    GetTicketQueryHandler getTicket,
    ILogger<EscalateTicketCommandHandler> logger)
{
    public async Task<EscalateTicketResponseDto> HandleAsync(
        int ticketId,
        EscalateTicketRequest request,
        CancellationToken ct,
        string? authenticatedActor = null)
    {
        var trimmedReason = request.Reason?.Trim() ?? string.Empty;
        if (trimmedReason.Length is < 5 or > 500)
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["Reason"] = ["Escalation reason must be between 5 and 500 characters."]
            });
        }

        var actor = (!string.IsNullOrWhiteSpace(authenticatedActor) ? authenticatedActor : request.EscalatedBy)?.Trim();
        if (string.IsNullOrWhiteSpace(actor) || actor.Length > 100)
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["EscalatedBy"] = ["EscalatedBy is required and must be at most 100 characters."]
            });
        }

        var ticket = await tickets.GetByIdAsync(ticketId, ct)
            ?? throw new NotFoundException("Ticket", ticketId);

        var customer = await customers.GetByIdAsync(ticket.CustomerId, ct)
            ?? throw new NotFoundException("Customer", ticket.CustomerId);

        var category = await categories.GetByIdAsync(ticket.CategoryId, ct)
            ?? throw new NotFoundException("Category", ticket.CategoryId);

        var workloads = await agents.GetWorkloadsAsync(ct);
        var fromAgent = ticket.AssignedAgentId.HasValue
            ? workloads.FirstOrDefault(w => w.Agent.Id == ticket.AssignedAgentId.Value)?.Agent
              ?? await agents.GetByIdAsync(ticket.AssignedAgentId.Value, ct)
            : null;

        var assignmentDecision = TicketTriagePolicy.ReevaluateAgentForEscalation(
            ticket.AssignedAgentId,
            category,
            workloads);

        var now = clock.UtcNow;

        var escalation = ticket.Escalate(
            slaPolicy,
            customer.Tier,
            assignmentDecision.Agent?.Id,
            trimmedReason,
            actor,
            now);

        await tickets.AddEscalationAsync(escalation, ct);
        await unitOfWork.SaveChangesAsync(ct);

        logger.LogInformation(
            "Ticket {Reference} escalated from {FromPriority} to {ToPriority} by {Actor}.",
            ticket.Reference,
            escalation.FromPriority,
            escalation.ToPriority,
            escalation.EscalatedBy);

        var updatedTicket = await getTicket.HandleAsync(ticketId, ct);

        var escalationDto = new TicketEscalationDto
        {
            Id = escalation.Id,
            TicketId = ticket.Id,
            FromPriority = escalation.FromPriority,
            ToPriority = escalation.ToPriority,
            FromAgent = fromAgent is null ? null : new AgentSummaryDto(fromAgent.Id, fromAgent.FullName),
            ToAgent = assignmentDecision.Agent is null
                ? null
                : new AgentSummaryDto(assignmentDecision.Agent.Id, assignmentDecision.Agent.FullName),
            FromDueAtUtc = escalation.FromDueAtUtc,
            ToDueAtUtc = escalation.ToDueAtUtc,
            Reason = escalation.Reason,
            EscalatedBy = escalation.EscalatedBy,
            EscalatedAtUtc = escalation.EscalatedAtUtc
        };

        return new EscalateTicketResponseDto(updatedTicket, escalationDto);
    }
}

