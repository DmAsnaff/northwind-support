using Microsoft.EntityFrameworkCore;
using SupportDesk.Application.Abstractions;
using SupportDesk.Application.Contracts.Common;
using SupportDesk.Application.Contracts.Tickets;
using SupportDesk.Domain.Aggregates.Agents;
using SupportDesk.Domain.Aggregates.Categories;
using SupportDesk.Domain.Aggregates.Customers;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.Infrastructure.Data;

namespace SupportDesk.Infrastructure.Queries;

/// <summary>
/// Ticket reads. Projected in the database and never tracked. Aggregates hold only each
/// other's ids, so the customer, category and agent labels are joined in here, on the read side.
/// </summary>
public sealed class TicketQueries(SupportDbContext db, IClock clock, SlaPolicy? slaPolicy = null) : ITicketQueries
{
    private readonly SlaPolicy _slaPolicy = slaPolicy ?? new SlaPolicy(new SlaSettings());

    /// <remarks>
    /// Filters, sorts and pages the ticket list. Every supplied filter is applied in the
    /// database before counting and paging, so <c>totalCount</c> and <c>totalPages</c>
    /// reflect the filtered set.
    /// </remarks>
    public async Task<PagedResult<TicketListItemDto>> GetPagedAsync(TicketQuery query, CancellationToken ct)
    {
        var now = clock.UtcNow;

        var tickets = ApplyFilters(TicketsWithLabels(), query, now);

        var totalCount = await tickets.CountAsync(ct);

        var rawItems = await ApplySort(tickets, query)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(x => new TicketListItemDto
            {
                Id = x.Ticket.Id,
                Reference = x.Ticket.Reference,
                Title = x.Ticket.Title,
                Status = x.Ticket.Status,
                Priority = x.Ticket.Priority,
                Customer = new CustomerSummaryDto(x.Customer.Id, x.Customer.Name, x.Customer.Tier),
                Category = new CategorySummaryDto(x.Category.Id, x.Category.Name),
                AssignedAgent = x.Agent == null ? null : new AgentSummaryDto(x.Agent.Id, x.Agent.FullName),
                CreatedAtUtc = x.Ticket.CreatedAtUtc,
                UpdatedAtUtc = x.Ticket.UpdatedAtUtc,
                DueAtUtc = x.Ticket.DueAtUtc,
                ResolvedAtUtc = x.Ticket.ResolvedAtUtc
            })
            .ToListAsync(ct);

        foreach (var item in rawItems)
        {
            item.SlaStatus = SlaEvaluator.Evaluate(
                item.DueAtUtc,
                item.ResolvedAtUtc,
                now,
                _slaPolicy.GetWindow(item.Priority, item.Customer.Tier),
                _slaPolicy.AtRiskThresholdPercent);
        }

        return new PagedResult<TicketListItemDto>(rawItems, query.Page, query.PageSize, totalCount);
    }

    public async Task<TicketDetailDto?> GetDetailAsync(int id, CancellationToken ct)
    {
        var now = clock.UtcNow;

        var detail = await TicketsWithLabels()
            .Where(x => x.Ticket.Id == id)
            .Select(x => new TicketDetailDto
            {
                Id = x.Ticket.Id,
                Reference = x.Ticket.Reference,
                Title = x.Ticket.Title,
                Description = x.Ticket.Description,
                Status = x.Ticket.Status,
                Priority = x.Ticket.Priority,
                Customer = new CustomerContactDto(
                    x.Customer.Id, x.Customer.Name, x.Customer.Email, x.Customer.Phone, x.Customer.Tier),
                Category = new CategoryDto(x.Category.Id, x.Category.Name, x.Category.RequiresSpecialist),
                AssignedAgent = x.Agent == null ? null : new AgentSummaryDto(x.Agent.Id, x.Agent.FullName),
                CreatedAtUtc = x.Ticket.CreatedAtUtc,
                UpdatedAtUtc = x.Ticket.UpdatedAtUtc,
                DueAtUtc = x.Ticket.DueAtUtc,
                ResolvedAtUtc = x.Ticket.ResolvedAtUtc
            })
            .FirstOrDefaultAsync(ct);

        if (detail is not null)
        {
            detail.SlaStatus = SlaEvaluator.Evaluate(
                detail.DueAtUtc,
                detail.ResolvedAtUtc,
                now,
                _slaPolicy.GetWindow(detail.Priority, detail.Customer.Tier),
                _slaPolicy.AtRiskThresholdPercent);
        }

        return detail;
    }

    public async Task<IReadOnlyList<TicketListItemDto>> GetForCustomerAsync(int customerId, CancellationToken ct)
    {
        var now = clock.UtcNow;

        var items = await TicketsWithLabels()
            .Where(x => x.Ticket.CustomerId == customerId)
            .OrderByDescending(x => x.Ticket.CreatedAtUtc)
            .ThenByDescending(x => x.Ticket.Id)
            .Select(x => new TicketListItemDto
            {
                Id = x.Ticket.Id,
                Reference = x.Ticket.Reference,
                Title = x.Ticket.Title,
                Status = x.Ticket.Status,
                Priority = x.Ticket.Priority,
                Customer = new CustomerSummaryDto(x.Customer.Id, x.Customer.Name, x.Customer.Tier),
                Category = new CategorySummaryDto(x.Category.Id, x.Category.Name),
                AssignedAgent = x.Agent == null ? null : new AgentSummaryDto(x.Agent.Id, x.Agent.FullName),
                CreatedAtUtc = x.Ticket.CreatedAtUtc,
                UpdatedAtUtc = x.Ticket.UpdatedAtUtc,
                DueAtUtc = x.Ticket.DueAtUtc,
                ResolvedAtUtc = x.Ticket.ResolvedAtUtc
            })
            .ToListAsync(ct);

        foreach (var item in items)
        {
            item.SlaStatus = SlaEvaluator.Evaluate(
                item.DueAtUtc,
                item.ResolvedAtUtc,
                now,
                _slaPolicy.GetWindow(item.Priority, item.Customer.Tier),
                _slaPolicy.AtRiskThresholdPercent);
        }

        return items;
    }

    public async Task<IReadOnlyList<TicketEscalationDto>?> GetEscalationsAsync(int ticketId, CancellationToken ct)
    {
        var exists = await db.Set<Ticket>()
            .AsNoTracking()
            .AnyAsync(t => t.Id == ticketId, ct);

        if (!exists)
        {
            return null;
        }

        var query =
            from escalation in db.Set<TicketEscalation>().AsNoTracking()
            where escalation.TicketId == ticketId
            from fromAgent in db.Set<Agent>().AsNoTracking()
                .Where(a => a.Id == escalation.FromAgentId)
                .DefaultIfEmpty()
            from toAgent in db.Set<Agent>().AsNoTracking()
                .Where(a => a.Id == escalation.ToAgentId)
                .DefaultIfEmpty()
            orderby escalation.EscalatedAtUtc descending, escalation.Id descending
            select new TicketEscalationDto
            {
                Id = escalation.Id,
                TicketId = escalation.TicketId,
                FromPriority = escalation.FromPriority,
                ToPriority = escalation.ToPriority,
                FromAgent = fromAgent == null ? null : new AgentSummaryDto(fromAgent.Id, fromAgent.FullName),
                ToAgent = toAgent == null ? null : new AgentSummaryDto(toAgent.Id, toAgent.FullName),
                FromDueAtUtc = escalation.FromDueAtUtc,
                ToDueAtUtc = escalation.ToDueAtUtc,
                Reason = escalation.Reason,
                EscalatedBy = escalation.EscalatedBy,
                EscalatedAtUtc = escalation.EscalatedAtUtc
            };

        return await query.ToListAsync(ct);
    }

    /// <summary>
    /// Every ticket with the rows that label it. Filter and sort this, then project.
    /// </summary>
    private IQueryable<TicketWithLabels> TicketsWithLabels() =>
        from ticket in db.Set<Ticket>().AsNoTracking()
        join customer in db.Set<Customer>() on ticket.CustomerId equals customer.Id
        join category in db.Set<Category>() on ticket.CategoryId equals category.Id
        from agent in db.Set<Agent>().Where(a => a.Id == ticket.AssignedAgentId).DefaultIfEmpty()
        select new TicketWithLabels { Ticket = ticket, Customer = customer, Category = category, Agent = agent };

    /// <summary>
    /// Applies every supplied filter to the query. All conditions combine with AND.
    /// Runs entirely in SQL - no in-memory filtering.
    /// </summary>
    private IQueryable<TicketWithLabels> ApplyFilters(
        IQueryable<TicketWithLabels> source,
        TicketQuery query,
        DateTime nowUtc)
    {
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim().ToLower();
            source = source.Where(x =>
                x.Ticket.Title.ToLower().Contains(term) ||
                x.Ticket.Reference.ToLower().Contains(term) ||
                x.Customer.Name.ToLower().Contains(term));
        }

        if (query.Status.HasValue)
        {
            source = source.Where(x => x.Ticket.Status == query.Status.Value);
        }

        if (query.Priority.HasValue)
        {
            source = source.Where(x => x.Ticket.Priority == query.Priority.Value);
        }

        if (query.CategoryId.HasValue)
        {
            source = source.Where(x => x.Ticket.CategoryId == query.CategoryId.Value);
        }

        if (query.CustomerId.HasValue)
        {
            source = source.Where(x => x.Ticket.CustomerId == query.CustomerId.Value);
        }

        if (query.UnassignedOnly)
        {
            source = source.Where(x => x.Ticket.AssignedAgentId == null);
        }
        else if (query.AssignedAgentId.HasValue)
        {
            source = source.Where(x => x.Ticket.AssignedAgentId == query.AssignedAgentId.Value);
        }

        if (query.SlaStatus.HasValue)
        {
            source = ApplySlaFilter(source, query.SlaStatus.Value, nowUtc);
        }

        return source;
    }

    private IQueryable<TicketWithLabels> ApplySlaFilter(
        IQueryable<TicketWithLabels> source,
        SlaStatus slaStatus,
        DateTime nowUtc)
    {
        var stdLowCutoff = nowUtc.Add(_slaPolicy.GetAtRiskThreshold(TicketPriority.Low, CustomerTier.Standard));
        var stdMedCutoff = nowUtc.Add(_slaPolicy.GetAtRiskThreshold(TicketPriority.Medium, CustomerTier.Standard));
        var stdHighCutoff = nowUtc.Add(_slaPolicy.GetAtRiskThreshold(TicketPriority.High, CustomerTier.Standard));
        var stdCritCutoff = nowUtc.Add(_slaPolicy.GetAtRiskThreshold(TicketPriority.Critical, CustomerTier.Standard));

        var premLowCutoff = nowUtc.Add(_slaPolicy.GetAtRiskThreshold(TicketPriority.Low, CustomerTier.Premium));
        var premMedCutoff = nowUtc.Add(_slaPolicy.GetAtRiskThreshold(TicketPriority.Medium, CustomerTier.Premium));
        var premHighCutoff = nowUtc.Add(_slaPolicy.GetAtRiskThreshold(TicketPriority.High, CustomerTier.Premium));
        var premCritCutoff = nowUtc.Add(_slaPolicy.GetAtRiskThreshold(TicketPriority.Critical, CustomerTier.Premium));

        return slaStatus switch
        {
            SlaStatus.NotApplicable => source.Where(x => x.Ticket.DueAtUtc == null),
            SlaStatus.Met => source.Where(x =>
                x.Ticket.DueAtUtc != null
                && x.Ticket.ResolvedAtUtc != null
                && x.Ticket.ResolvedAtUtc <= x.Ticket.DueAtUtc),
            SlaStatus.Breached => source.Where(x =>
                x.Ticket.DueAtUtc != null
                && ((x.Ticket.ResolvedAtUtc != null && x.Ticket.ResolvedAtUtc > x.Ticket.DueAtUtc)
                    || (x.Ticket.ResolvedAtUtc == null && nowUtc > x.Ticket.DueAtUtc))),
            SlaStatus.AtRisk => source.Where(x =>
                x.Ticket.DueAtUtc != null
                && x.Ticket.ResolvedAtUtc == null
                && x.Ticket.DueAtUtc >= nowUtc
                && ((x.Customer.Tier == CustomerTier.Standard && x.Ticket.Priority == TicketPriority.Low && x.Ticket.DueAtUtc <= stdLowCutoff)
                    || (x.Customer.Tier == CustomerTier.Standard && x.Ticket.Priority == TicketPriority.Medium && x.Ticket.DueAtUtc <= stdMedCutoff)
                    || (x.Customer.Tier == CustomerTier.Standard && x.Ticket.Priority == TicketPriority.High && x.Ticket.DueAtUtc <= stdHighCutoff)
                    || (x.Customer.Tier == CustomerTier.Standard && x.Ticket.Priority == TicketPriority.Critical && x.Ticket.DueAtUtc <= stdCritCutoff)
                    || (x.Customer.Tier == CustomerTier.Premium && x.Ticket.Priority == TicketPriority.Low && x.Ticket.DueAtUtc <= premLowCutoff)
                    || (x.Customer.Tier == CustomerTier.Premium && x.Ticket.Priority == TicketPriority.Medium && x.Ticket.DueAtUtc <= premMedCutoff)
                    || (x.Customer.Tier == CustomerTier.Premium && x.Ticket.Priority == TicketPriority.High && x.Ticket.DueAtUtc <= premHighCutoff)
                    || (x.Customer.Tier == CustomerTier.Premium && x.Ticket.Priority == TicketPriority.Critical && x.Ticket.DueAtUtc <= premCritCutoff))),
            SlaStatus.WithinSla => source.Where(x =>
                x.Ticket.DueAtUtc != null
                && x.Ticket.ResolvedAtUtc == null
                && ((x.Customer.Tier == CustomerTier.Standard && x.Ticket.Priority == TicketPriority.Low && x.Ticket.DueAtUtc > stdLowCutoff)
                    || (x.Customer.Tier == CustomerTier.Standard && x.Ticket.Priority == TicketPriority.Medium && x.Ticket.DueAtUtc > stdMedCutoff)
                    || (x.Customer.Tier == CustomerTier.Standard && x.Ticket.Priority == TicketPriority.High && x.Ticket.DueAtUtc > stdHighCutoff)
                    || (x.Customer.Tier == CustomerTier.Standard && x.Ticket.Priority == TicketPriority.Critical && x.Ticket.DueAtUtc > stdCritCutoff)
                    || (x.Customer.Tier == CustomerTier.Premium && x.Ticket.Priority == TicketPriority.Low && x.Ticket.DueAtUtc > premLowCutoff)
                    || (x.Customer.Tier == CustomerTier.Premium && x.Ticket.Priority == TicketPriority.Medium && x.Ticket.DueAtUtc > premMedCutoff)
                    || (x.Customer.Tier == CustomerTier.Premium && x.Ticket.Priority == TicketPriority.High && x.Ticket.DueAtUtc > premHighCutoff)
                    || (x.Customer.Tier == CustomerTier.Premium && x.Ticket.Priority == TicketPriority.Critical && x.Ticket.DueAtUtc > premCritCutoff))),
            _ => source
        };
    }

    private static IQueryable<TicketWithLabels> ApplySort(IQueryable<TicketWithLabels> source, TicketQuery query)
    {
        var descending = !string.Equals(query.SortDirection, "asc", StringComparison.OrdinalIgnoreCase);

        var sorted = query.SortBy?.ToLowerInvariant() switch
        {
            "updatedatutc" => descending
                ? source.OrderByDescending(x => x.Ticket.UpdatedAtUtc)
                : source.OrderBy(x => x.Ticket.UpdatedAtUtc),
            "dueatutc" => descending
                ? source.OrderByDescending(x => x.Ticket.DueAtUtc)
                : source.OrderBy(x => x.Ticket.DueAtUtc),
            "priority" => descending
                ? source.OrderByDescending(x => x.Ticket.Priority)
                : source.OrderBy(x => x.Ticket.Priority),
            "status" => descending
                ? source.OrderByDescending(x => x.Ticket.Status)
                : source.OrderBy(x => x.Ticket.Status),
            _ => descending
                ? source.OrderByDescending(x => x.Ticket.CreatedAtUtc)
                : source.OrderBy(x => x.Ticket.CreatedAtUtc)
        };

        // Keeps paging stable when the sort column has duplicates.
        return sorted.ThenByDescending(x => x.Ticket.Id);
    }

    private sealed class TicketWithLabels
    {
        public required Ticket Ticket { get; init; }

        public required Customer Customer { get; init; }

        public required Category Category { get; init; }

        public Agent? Agent { get; init; }
    }
}
