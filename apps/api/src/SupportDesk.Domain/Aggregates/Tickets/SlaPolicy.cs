using System.Globalization;
using SupportDesk.Domain.Aggregates.Customers;

namespace SupportDesk.Domain.Aggregates.Tickets;

/// <summary>
/// Calculates SLA response windows and due dates from configurable <see cref="SlaSettings"/>.
/// Shared by ticket creation (triage) and ticket escalation.
/// </summary>
public sealed class SlaPolicy(SlaSettings settings)
{
    public SlaSettings Settings { get; } = settings;

    public double AtRiskThresholdPercent => Settings.AtRiskThresholdPercent;

    /// <summary>
    /// Computes the SLA response window for a priority and customer tier (BR-2, BR-3, BR-6).
    /// </summary>
    public TimeSpan GetWindow(TicketPriority priority, CustomerTier customerTier)
    {
        var baseHours = Settings.GetBaseHours(priority);
        var adjustedHours = customerTier == CustomerTier.Premium
            ? baseHours * Settings.PremiumCustomerMultiplier
            : baseHours;

        var effectiveHours = Math.Max(adjustedHours, Settings.MinimumWindowHours);
        return TimeSpan.FromHours(effectiveHours);
    }

    /// <summary>
    /// Computes the UTC due date starting from <paramref name="fromUtc"/>.
    /// </summary>
    public DateTime CalculateDueAtUtc(DateTime fromUtc, TicketPriority priority, CustomerTier customerTier) =>
        fromUtc.Add(GetWindow(priority, customerTier));

    /// <summary>
    /// Computes the remaining-time threshold at or below which an open ticket is considered <see cref="SlaStatus.AtRisk"/>.
    /// </summary>
    public TimeSpan GetAtRiskThreshold(TicketPriority priority, CustomerTier customerTier)
    {
        var window = GetWindow(priority, customerTier);
        return TimeSpan.FromTicks((long)(window.Ticks * (Settings.AtRiskThresholdPercent / 100.0)));
    }

    /// <summary>
    /// Explains how the SLA window was derived for triage output.
    /// </summary>
    public string DescribeWindow(TicketPriority priority, CustomerTier customerTier)
    {
        var baseHours = Settings.GetBaseHours(priority);
        var window = GetWindow(priority, customerTier);
        var effectiveHours = window.TotalHours;

        if (customerTier == CustomerTier.Premium)
        {
            var rawPremiumHours = baseHours * Settings.PremiumCustomerMultiplier;
            if (rawPremiumHours < Settings.MinimumWindowHours)
            {
                return string.Create(
                    CultureInfo.InvariantCulture,
                    $"{effectiveHours:0.##}h SLA window ({priority} base {baseHours:0.##}h × {Settings.PremiumCustomerMultiplier:0.##} for Premium customer, floored at {Settings.MinimumWindowHours:0.##}h minimum).");
            }

            return string.Create(
                CultureInfo.InvariantCulture,
                $"{effectiveHours:0.##}h SLA window ({priority} base {baseHours:0.##}h × {Settings.PremiumCustomerMultiplier:0.##} for Premium customer).");
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{effectiveHours:0.##}h SLA window ({priority} base {baseHours:0.##}h for Standard customer).");
    }
}

