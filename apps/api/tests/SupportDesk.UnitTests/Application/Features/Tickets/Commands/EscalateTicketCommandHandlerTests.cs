using SupportDesk.Application.Contracts.Tickets;
using SupportDesk.Application.Exceptions;
using SupportDesk.Domain.Aggregates.Customers;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.Domain.Exceptions;
using SupportDesk.UnitTests.TestDoubles;

namespace SupportDesk.UnitTests.Application.Features.Tickets.Commands;

public class EscalateTicketCommandHandlerTests
{
    [Fact]
    public async Task HandleAsync_WhenCurrentAgentStillEligible_KeepsAgentRaisesPriorityAndWritesHistory()
    {
        var context = new TicketCommandTestContext();
        context.WithCustomer(1, CustomerTier.Standard);
        context.WithCategory(2, "Billing", requiresSpecialist: true);

        var sara = context.CreateAgent(4, "Sara Lindqvist", maxOpenTickets: 6, isActive: true, 2);
        var alex = context.CreateAgent(1, "Alex Turner", maxOpenTickets: 10, isActive: true, 1);

        context.Agents.Setup(a => a.GetWorkloadsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new AgentWorkload(alex, 1),
                new AgentWorkload(sara, 3)
            ]);

        var ticket = context.ExistingTicket(
            new TicketBuilder()
                .WithPriority(TicketPriority.Medium)
                .WithStatus(TicketStatus.InProgress)
                .AssignedTo(sara.Id)
                .Build());

        context.Clock.Advance(TimeSpan.FromHours(2));

        var response = await context.EscalateTicket.HandleAsync(
            ticket.Id,
            new EscalateTicketRequest("Customer finance team is blocked on closing.", "Priya Nair"),
            CancellationToken.None);

        Assert.Equal(TicketPriority.High, ticket.Priority);
        Assert.Equal(sara.Id, ticket.AssignedAgentId);
        Assert.Equal(context.Clock.UtcNow.AddHours(8), ticket.DueAtUtc);

        Assert.NotNull(context.AddedEscalation);
        Assert.Equal(TicketPriority.Medium, response.Escalation.FromPriority);
        Assert.Equal(TicketPriority.High, response.Escalation.ToPriority);
        Assert.Equal(sara.Id, response.Escalation.FromAgent?.Id);
        Assert.Equal(sara.Id, response.Escalation.ToAgent?.Id);
        Assert.Equal("Priya Nair", response.Escalation.EscalatedBy);
        context.VerifySaved(Times.Once());
    }

    [Fact]
    public async Task HandleAsync_WhenCurrentAgentNotEligible_ReassignsPerBr5()
    {
        var context = new TicketCommandTestContext();
        context.WithCustomer(1, CustomerTier.Premium);
        context.WithCategory(2, "Billing", requiresSpecialist: true);

        // Current owner Alex is not a Billing specialist; Sara is an active Billing specialist.
        var alex = context.CreateAgent(1, "Alex Turner", maxOpenTickets: 10, isActive: true, 1);
        var sara = context.CreateAgent(4, "Sara Lindqvist", maxOpenTickets: 6, isActive: true, 2);

        context.Agents.Setup(a => a.GetWorkloadsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new AgentWorkload(alex, 2),
                new AgentWorkload(sara, 2)
            ]);

        var ticket = context.ExistingTicket(
            new TicketBuilder()
                .WithCategoryId(2)
                .WithPriority(TicketPriority.Low)
                .WithStatus(TicketStatus.Open)
                .AssignedTo(alex.Id)
                .Build());

        var response = await context.EscalateTicket.HandleAsync(
            ticket.Id,
            new EscalateTicketRequest("Needs immediate billing specialist attention.", "Alex Turner"),
            CancellationToken.None);

        Assert.Equal(TicketPriority.Medium, ticket.Priority);
        Assert.Equal(sara.Id, ticket.AssignedAgentId);
        Assert.Equal(alex.Id, response.Escalation.FromAgent?.Id);
        Assert.Equal(sara.Id, response.Escalation.ToAgent?.Id);
    }

    [Fact]
    public async Task HandleAsync_WhenReasonIsTooShort_ThrowsValidationException()
    {
        var context = new TicketCommandTestContext();

        await Assert.ThrowsAsync<ValidationException>(() =>
            context.EscalateTicket.HandleAsync(
                1,
                new EscalateTicketRequest("bad", "Alex Turner"),
                CancellationToken.None));

        context.VerifySaved(Times.Never());
    }

    [Fact]
    public async Task HandleAsync_WhenTicketIsCritical_ThrowsBusinessRuleViolation()
    {
        var context = new TicketCommandTestContext();
        var ticket = context.ExistingTicket(
            new TicketBuilder()
                .WithPriority(TicketPriority.Critical)
                .WithStatus(TicketStatus.Open)
                .Build());

        await Assert.ThrowsAsync<BusinessRuleViolationException>(() =>
            context.EscalateTicket.HandleAsync(
                ticket.Id,
                new EscalateTicketRequest("Cannot escalate critical further.", "Alex Turner"),
                CancellationToken.None));

        context.VerifySaved(Times.Never());
    }
}
