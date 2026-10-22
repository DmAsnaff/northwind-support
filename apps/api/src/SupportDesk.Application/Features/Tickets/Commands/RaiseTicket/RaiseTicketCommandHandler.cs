using Microsoft.Extensions.Logging;
using SupportDesk.Application.Abstractions;
using SupportDesk.Application.Contracts.Tickets;
using SupportDesk.Application.Exceptions;
using SupportDesk.Application.Features.Tickets.Queries.GetTicket;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.Domain.Repositories;

namespace SupportDesk.Application.Features.Tickets.Commands.RaiseTicket;

/// <summary>
/// Raises a ticket for a customer and runs automatic triage (priority, SLA due date, and agent assignment).
/// </summary>
public sealed class RaiseTicketCommandHandler(
    ITicketRepository tickets,
    ICustomerRepository customers,
    ICategoryRepository categories,
    IAgentRepository agents,
    SlaPolicy slaPolicy,
    IUnitOfWork unitOfWork,
    IClock clock,
    GetTicketQueryHandler getTicket,
    ILogger<RaiseTicketCommandHandler> logger)
{
    /// <exception cref="NotFoundException">The customer or the category does not exist.</exception>
    public async Task<TicketDetailDto> HandleAsync(CreateTicketRequest request, CancellationToken ct)
    {
        var customer = await customers.GetByIdAsync(request.CustomerId, ct)
            ?? throw new NotFoundException("Customer", request.CustomerId);

        var category = await categories.GetByIdAsync(request.CategoryId, ct)
            ?? throw new NotFoundException("Category", request.CategoryId);

        var now = clock.UtcNow;

        var priorityDecision = TicketTriagePolicy.ResolvePriority(request.RequestedPriority, category);
        var dueAtUtc = slaPolicy.CalculateDueAtUtc(now, priorityDecision.Priority, customer.Tier);
        var slaReason = slaPolicy.DescribeWindow(priorityDecision.Priority, customer.Tier);

        var workloads = await agents.GetWorkloadsAsync(ct);
        var assignmentDecision = TicketTriagePolicy.SelectAgent(category, workloads);

        var ticket = Ticket.Raise(
            await tickets.NextReferenceAsync(ct),
            request.Title,
            request.Description,
            request.CustomerId,
            request.CategoryId,
            priorityDecision.Priority,
            now,
            dueAtUtc,
            assignmentDecision.Agent?.Id);

        await tickets.AddAsync(ticket, ct);
        await unitOfWork.SaveChangesAsync(ct);

        logger.LogInformation(
            "Ticket {Reference} raised for customer {CustomerId} in category {CategoryId} (Priority={Priority}, AgentId={AgentId}, DueAtUtc={DueAtUtc}).",
            ticket.Reference,
            ticket.CustomerId,
            ticket.CategoryId,
            ticket.Priority,
            ticket.AssignedAgentId,
            ticket.DueAtUtc);

        var detail = await getTicket.HandleAsync(ticket.Id, ct);
        detail.Triage = new TriageResultDto(
            priorityDecision.Reason,
            slaReason,
            assignmentDecision.Reason);

        return detail;
    }
}
