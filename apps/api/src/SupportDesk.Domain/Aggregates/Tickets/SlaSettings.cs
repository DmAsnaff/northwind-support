namespace SupportDesk.Domain.Aggregates.Tickets;

/// <summary>
/// Configurable response-window settings bound from the <c>Sla</c> configuration section.
/// </summary>
public sealed class SlaSettings
{
    public const string SectionName = "Sla";

    public Dictionary<TicketPriority, double> BaseWindowHours { get; set; } = new()
    {
        [TicketPriority.Low] = 72,
        [TicketPriority.Medium] = 24,
        [TicketPriority.High] = 8,
        [TicketPriority.Critical] = 4
    };

    public double PremiumCustomerMultiplier { get; set; } = 0.5;

    public double MinimumWindowHours { get; set; } = 1.0;

    public double AtRiskThresholdPercent { get; set; } = 25.0;

    public double GetBaseHours(TicketPriority priority) =>
        BaseWindowHours.TryGetValue(priority, out var hours)
            ? hours
            : priority switch
            {
                TicketPriority.Low => 72,
                TicketPriority.Medium => 24,
                TicketPriority.High => 8,
                TicketPriority.Critical => 4,
                _ => 24
            };
}

