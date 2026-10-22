using SupportDesk.Application.Contracts.Tickets;
using SupportDesk.Application.Exceptions;
using SupportDesk.Domain.Aggregates.Customers;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.UnitTests.TestDoubles;

namespace SupportDesk.UnitTests.Application.Features.Tickets.Commands;

public class RaiseTicketCommandHandlerTests
{
    private static CreateTicketRequest Request(TicketPriority? priority = null) =>
        new("Cannot log in to the portal", "Happens for every user since this morning.", 1, 3, priority);

    [Fact]
    public async Task HandleAsync_StampsReferenceStatusAndTimestamps()
    {
        var context = new TicketCommandTestContext();
        context.WithCustomer(1, CustomerTier.Premium);
        context.WithCategory(3, "Technical", requiresSpecialist: false, forcesCriticalPriority: false);
        var alex = context.CreateAgent(1, "Alex Turner", maxOpenTickets: 10);
        context.Agents.Setup(a => a.GetWorkloadsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([new AgentWorkload(alex, 2)]);

        var dto = await context.RaiseTicket.HandleAsync(Request(TicketPriority.High), CancellationToken.None);

        var created = context.AddedTicket;
        Assert.NotNull(created);
        Assert.Equal("TCK-0041", created.Reference);
        Assert.Equal(TicketStatus.New, created.Status);
        Assert.Equal(TicketPriority.High, created.Priority);
        Assert.Equal(1, created.AssignedAgentId);
        // High (8h) * 0.5 (Premium) = 4h
        Assert.Equal(FixedClock.DefaultNow.AddHours(4), created.DueAtUtc);
        Assert.Equal(FixedClock.DefaultNow, created.CreatedAtUtc);
        Assert.Equal(FixedClock.DefaultNow, created.UpdatedAtUtc);
        Assert.NotNull(dto.Triage);
        context.VerifySaved(Times.Once());
    }

    [Fact]
    public async Task HandleAsync_WithoutRequestedPriority_FallsBackToMedium()
    {
        var context = new TicketCommandTestContext();

        await context.RaiseTicket.HandleAsync(Request(), CancellationToken.None);

        Assert.NotNull(context.AddedTicket);
        Assert.Equal(TicketPriority.Medium, context.AddedTicket.Priority);
    }

    [Fact]
    public async Task HandleAsync_WhenCategoryForcesCritical_OverridesRequestedPriority()
    {
        var context = new TicketCommandTestContext();
        context.WithCustomer(1, CustomerTier.Standard);
        context.WithCategory(5, "Security", requiresSpecialist: true, forcesCriticalPriority: true);

        var result = await context.RaiseTicket.HandleAsync(
            new CreateTicketRequest("API key leaked", "Committed to public repo.", 1, 5, TicketPriority.Low),
            CancellationToken.None);

        Assert.NotNull(context.AddedTicket);
        Assert.Equal(TicketPriority.Critical, context.AddedTicket.Priority);
        Assert.Equal(FixedClock.DefaultNow.AddHours(4), context.AddedTicket.DueAtUtc);
        Assert.Null(context.AddedTicket.AssignedAgentId);
        Assert.NotNull(result.Triage);
        Assert.Contains("Critical", result.Triage.PriorityReason);
        Assert.Contains("Unassigned", result.Triage.AssignmentReason);
    }

    [Fact]
    public async Task HandleAsync_WhenCustomerDoesNotExist_Throws()
    {
        var context = new TicketCommandTestContext();
        context.Customers
            .Setup(c => c.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Customer?)null);

        var exception = await Assert.ThrowsAsync<NotFoundException>(
            () => context.RaiseTicket.HandleAsync(Request(), CancellationToken.None));

        Assert.Contains("Customer", exception.Message);
        context.VerifySaved(Times.Never());
    }
}
