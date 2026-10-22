using SupportDesk.Application.Abstractions;
using SupportDesk.Application.Contracts.Tickets;
using SupportDesk.Application.Exceptions;

namespace SupportDesk.Application.Features.Tickets.Queries.GetTicketEscalations;

/// <summary>
/// Returns a ticket's escalation history, newest first.
/// </summary>
public sealed class GetTicketEscalationsQueryHandler(ITicketQueries queries)
{
    /// <exception cref="NotFoundException">The ticket does not exist.</exception>
    public async Task<IReadOnlyList<TicketEscalationDto>> HandleAsync(int ticketId, CancellationToken ct) =>
        await queries.GetEscalationsAsync(ticketId, ct)
        ?? throw new NotFoundException("Ticket", ticketId);
}

