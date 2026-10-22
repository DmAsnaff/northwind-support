using SupportDesk.Domain.Aggregates.Agents;
using SupportDesk.Domain.Aggregates.Categories;
using SupportDesk.Domain.Aggregates.Customers;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.Domain.Common;
using SupportDesk.Domain.Exceptions;
using SupportDesk.UnitTests.TestDoubles;

namespace SupportDesk.UnitTests.Domain.Aggregates.Tickets;

public class TicketTriagePolicyTests
{
    private static readonly DateTime Now = FixedClock.DefaultNow;

    private static Category CreateCategory(
        int id,
        string name,
        bool requiresSpecialist = false,
        bool forcesCriticalPriority = false)
    {
        var category = new Category(name, requiresSpecialist, forcesCriticalPriority);
        typeof(Entity).GetProperty(nameof(Entity.Id))!.SetValue(category, id);
        return category;
    }

    private static Agent CreateAgent(
        int id,
        string fullName,
        int maxOpenTickets = 8,
        bool isActive = true,
        params int[] categoryIds)
    {
        var agent = new Agent(fullName, $"agent{id}@northwind.example", maxOpenTickets, Now);
        typeof(Entity).GetProperty(nameof(Entity.Id))!.SetValue(agent, id);

        foreach (var categoryId in categoryIds)
        {
            agent.AddSpecialization(categoryId);
        }

        if (!isActive)
        {
            agent.Deactivate();
        }

        return agent;
    }

    [Fact]
    public void ResolvePriority_WhenCategoryForcesCritical_OverridesRequestedPriority()
    {
        var security = CreateCategory(5, "Security", requiresSpecialist: true, forcesCriticalPriority: true);

        var decision = TicketTriagePolicy.ResolvePriority(TicketPriority.Low, security);

        Assert.Equal(TicketPriority.Critical, decision.Priority);
        Assert.Contains("Security", decision.Reason);
    }

    [Fact]
    public void ResolvePriority_WhenNoPriorityRequested_DefaultsToMedium()
    {
        var general = CreateCategory(1, "General");

        var decision = TicketTriagePolicy.ResolvePriority(null, general);

        Assert.Equal(TicketPriority.Medium, decision.Priority);
    }

    [Fact]
    public void SelectAgent_ChoosesEligibleAgentWithFewestOpenTickets()
    {
        var general = CreateCategory(1, "General");
        var alex = CreateAgent(1, "Alex Turner", maxOpenTickets: 10);
        var sara = CreateAgent(4, "Sara Lindqvist", maxOpenTickets: 6);

        var decision = TicketTriagePolicy.SelectAgent(
            general,
            [
                new AgentWorkload(alex, OpenTicketCount: 5),
                new AgentWorkload(sara, OpenTicketCount: 2)
            ]);

        Assert.NotNull(decision.Agent);
        Assert.Equal(sara.Id, decision.Agent!.Id);
    }

    [Fact]
    public void SelectAgent_WhenCategoryRequiresSpecialist_ExcludesNonSpecialists()
    {
        var billing = CreateCategory(2, "Billing", requiresSpecialist: true);
        var alex = CreateAgent(1, "Alex Turner", maxOpenTickets: 10, isActive: true, categoryIds: 1);
        var sara = CreateAgent(4, "Sara Lindqvist", maxOpenTickets: 6, isActive: true, categoryIds: 2);

        var decision = TicketTriagePolicy.SelectAgent(
            billing,
            [
                new AgentWorkload(alex, OpenTicketCount: 0),
                new AgentWorkload(sara, OpenTicketCount: 4)
            ]);

        Assert.NotNull(decision.Agent);
        Assert.Equal(sara.Id, decision.Agent!.Id);
    }

    [Fact]
    public void SelectAgent_ExcludesInactiveAndAtLimitAgents()
    {
        var billing = CreateCategory(2, "Billing", requiresSpecialist: true);
        var marcusAtLimit = CreateAgent(5, "Marcus Doyle", maxOpenTickets: 6, isActive: true, categoryIds: 2);
        var yukiInactive = CreateAgent(6, "Yuki Tanaka", maxOpenTickets: 8, isActive: false, categoryIds: 2);

        var decision = TicketTriagePolicy.SelectAgent(
            billing,
            [
                new AgentWorkload(marcusAtLimit, OpenTicketCount: 6),
                new AgentWorkload(yukiInactive, OpenTicketCount: 0)
            ]);

        Assert.Null(decision.Agent);
        Assert.Contains("Unassigned", decision.Reason);
    }

    [Fact]
    public void SelectAgent_WhenOpenCountsTie_BreaksTieDeterministicallyByAgentId()
    {
        var outage = CreateCategory(4, "Outage", requiresSpecialist: true, forcesCriticalPriority: true);
        var priya = CreateAgent(3, "Priya Nair", maxOpenTickets: 8, isActive: true, categoryIds: 4);
        var ben = CreateAgent(2, "Ben Osei", maxOpenTickets: 8, isActive: true, categoryIds: 4);

        var decision = TicketTriagePolicy.SelectAgent(
            outage,
            [
                new AgentWorkload(priya, OpenTicketCount: 3),
                new AgentWorkload(ben, OpenTicketCount: 3)
            ]);

        Assert.NotNull(decision.Agent);
        Assert.Equal(ben.Id, decision.Agent!.Id);
    }

    [Fact]
    public void Escalate_RaisesPriorityOneStepRecomputesDueDateAndRecordsImmutableHistory()
    {
        var policy = new SlaPolicy(new SlaSettings());
        var ticket = new TicketBuilder()
            .WithPriority(TicketPriority.Medium)
            .WithStatus(TicketStatus.Open)
            .AssignedTo(4)
            .Build();

        var escalatedAt = Now.AddHours(3);
        var escalation = ticket.Escalate(
            policy,
            CustomerTier.Premium,
            toAgentId: 4,
            reason: "Customer production workflow is degraded.",
            escalatedBy: "Sara Lindqvist",
            nowUtc: escalatedAt);

        Assert.Equal(TicketPriority.High, ticket.Priority);
        // High (8h) * 0.5 Premium = 4h from escalation time
        Assert.Equal(escalatedAt.AddHours(4), ticket.DueAtUtc);
        Assert.Equal(escalatedAt, ticket.UpdatedAtUtc);

        Assert.Equal(TicketPriority.Medium, escalation.FromPriority);
        Assert.Equal(TicketPriority.High, escalation.ToPriority);
        Assert.Equal(4, escalation.FromAgentId);
        Assert.Equal(4, escalation.ToAgentId);
        Assert.Equal(escalatedAt.AddHours(4), escalation.ToDueAtUtc);
        Assert.Equal("Customer production workflow is degraded.", escalation.Reason);
        Assert.Equal("Sara Lindqvist", escalation.EscalatedBy);
        Assert.Equal(escalatedAt, escalation.EscalatedAtUtc);
    }

    [Fact]
    public void Escalate_WhenTicketIsAlreadyCritical_ThrowsBusinessRuleViolation()
    {
        var policy = new SlaPolicy(new SlaSettings());
        var ticket = new TicketBuilder()
            .WithPriority(TicketPriority.Critical)
            .WithStatus(TicketStatus.Open)
            .Build();

        var ex = Assert.Throws<BusinessRuleViolationException>(() =>
            ticket.Escalate(policy, CustomerTier.Standard, null, "Still urgent", "Alex Turner", Now));

        Assert.Contains("Critical", ex.Message);
    }

    [Theory]
    [InlineData(TicketStatus.Resolved)]
    [InlineData(TicketStatus.Closed)]
    public void Escalate_WhenTicketIsResolvedOrClosed_ThrowsBusinessRuleViolation(TicketStatus terminalStatus)
    {
        var policy = new SlaPolicy(new SlaSettings());
        var ticket = new TicketBuilder()
            .WithPriority(TicketPriority.Medium)
            .WithStatus(terminalStatus)
            .Build();

        var ex = Assert.Throws<BusinessRuleViolationException>(() =>
            ticket.Escalate(policy, CustomerTier.Standard, null, "Customer replied", "Alex Turner", Now));

        Assert.Contains(terminalStatus.ToString(), ex.Message);
    }
}

