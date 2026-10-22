using SupportDesk.Domain.Aggregates.Customers;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.UnitTests.TestDoubles;

namespace SupportDesk.UnitTests.Domain.Aggregates.Tickets;

public class SlaEvaluatorTests
{
    private static readonly DateTime Now = FixedClock.DefaultNow;

    [Fact]
    public void Evaluate_WithoutADueDate_IsNotApplicable() =>
        Assert.Equal(SlaStatus.NotApplicable, SlaEvaluator.Evaluate(null, null, Now));

    [Fact]
    public void Evaluate_ResolvedOnTime_AtExactBoundary_IsMet() =>
        Assert.Equal(SlaStatus.Met, SlaEvaluator.Evaluate(Now, Now, Now.AddDays(1)));

    [Fact]
    public void Evaluate_ResolvedLate_ByOneTick_IsBreached() =>
        Assert.Equal(SlaStatus.Breached, SlaEvaluator.Evaluate(Now, Now.AddTicks(1), Now));

    [Fact]
    public void Evaluate_OpenAndPastDue_ByOneTick_IsBreached() =>
        Assert.Equal(
            SlaStatus.Breached,
            SlaEvaluator.Evaluate(Now.AddTicks(-1), null, Now, TimeSpan.FromHours(4)));

    [Fact]
    public void Evaluate_OpenWithExactly25PercentRemaining_IsAtRisk()
    {
        var window = TimeSpan.FromHours(4);
        var dueAtUtc = Now.AddHours(1); // 1h of 4h = exactly 25% remaining

        var status = SlaEvaluator.Evaluate(dueAtUtc, null, Now, window, atRiskThresholdPercent: 25);

        Assert.Equal(SlaStatus.AtRisk, status);
    }

    [Fact]
    public void Evaluate_OpenAtExactDueTime_IsAtRisk()
    {
        var window = TimeSpan.FromHours(4);

        var status = SlaEvaluator.Evaluate(Now, null, Now, window, atRiskThresholdPercent: 25);

        Assert.Equal(SlaStatus.AtRisk, status);
    }

    [Fact]
    public void Evaluate_OpenWithOneTickOver25PercentRemaining_IsWithinSla()
    {
        var window = TimeSpan.FromHours(4);
        var dueAtUtc = Now.AddHours(1).AddTicks(1); // 1 tick above the 25% threshold

        var status = SlaEvaluator.Evaluate(dueAtUtc, null, Now, window, atRiskThresholdPercent: 25);

        Assert.Equal(SlaStatus.WithinSla, status);
    }

    [Fact]
    public void Evaluate_OpenAndBeforeDue_WithoutWindow_IsWithinSla() =>
        Assert.Equal(SlaStatus.WithinSla, SlaEvaluator.Evaluate(Now.AddHours(1), null, Now));

    [Theory]
    [InlineData(TicketPriority.Low, CustomerTier.Standard, 72)]
    [InlineData(TicketPriority.Medium, CustomerTier.Standard, 24)]
    [InlineData(TicketPriority.High, CustomerTier.Standard, 8)]
    [InlineData(TicketPriority.Critical, CustomerTier.Standard, 4)]
    [InlineData(TicketPriority.Low, CustomerTier.Premium, 36)]
    [InlineData(TicketPriority.Medium, CustomerTier.Premium, 12)]
    [InlineData(TicketPriority.High, CustomerTier.Premium, 4)]
    [InlineData(TicketPriority.Critical, CustomerTier.Premium, 2)]
    public void SlaPolicy_GetWindow_AppliesBaseWindowAndPremiumMultiplier(
        TicketPriority priority,
        CustomerTier tier,
        double expectedHours)
    {
        var policy = new SlaPolicy(new SlaSettings());

        var window = policy.GetWindow(priority, tier);

        Assert.Equal(TimeSpan.FromHours(expectedHours), window);
    }

    [Fact]
    public void SlaPolicy_GetWindow_EnforcesMinimumOneHourFloor()
    {
        var settings = new SlaSettings
        {
            BaseWindowHours = new Dictionary<TicketPriority, double>
            {
                [TicketPriority.Critical] = 1.0
            },
            PremiumCustomerMultiplier = 0.5,
            MinimumWindowHours = 1.0
        };
        var policy = new SlaPolicy(settings);

        var window = policy.GetWindow(TicketPriority.Critical, CustomerTier.Premium);

        Assert.Equal(TimeSpan.FromHours(1.0), window);
        Assert.Equal(Now.AddHours(1.0), policy.CalculateDueAtUtc(Now, TicketPriority.Critical, CustomerTier.Premium));
    }
}
