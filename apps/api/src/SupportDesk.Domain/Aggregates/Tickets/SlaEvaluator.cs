namespace SupportDesk.Domain.Aggregates.Tickets;

/// <summary>
/// Works out where a ticket stands against its due date. SLA status is always derived from
/// the ticket's dates and the current time - never stored.
/// </summary>
public static class SlaEvaluator
{
    public const double DefaultAtRiskThresholdPercent = 25.0;

    public static SlaStatus Evaluate(
        DateTime? dueAtUtc,
        DateTime? resolvedAtUtc,
        DateTime nowUtc,
        TimeSpan? slaWindow = null,
        double atRiskThresholdPercent = DefaultAtRiskThresholdPercent)
    {
        if (dueAtUtc is null)
        {
            return SlaStatus.NotApplicable;
        }

        if (resolvedAtUtc is not null)
        {
            return resolvedAtUtc <= dueAtUtc ? SlaStatus.Met : SlaStatus.Breached;
        }

        if (nowUtc > dueAtUtc)
        {
            return SlaStatus.Breached;
        }

        if (slaWindow is { Ticks: > 0 } window)
        {
            var remaining = dueAtUtc.Value - nowUtc;
            var threshold = TimeSpan.FromTicks((long)(window.Ticks * (atRiskThresholdPercent / 100.0)));
            if (remaining <= threshold)
            {
                return SlaStatus.AtRisk;
            }
        }

        return SlaStatus.WithinSla;
    }

    public static SlaStatus Evaluate(
        DateTime createdAtUtc,
        DateTime? dueAtUtc,
        DateTime? resolvedAtUtc,
        DateTime nowUtc,
        double atRiskThresholdPercent = DefaultAtRiskThresholdPercent)
    {
        var window = dueAtUtc.HasValue && dueAtUtc.Value > createdAtUtc
            ? dueAtUtc.Value - createdAtUtc
            : (TimeSpan?)null;

        return Evaluate(dueAtUtc, resolvedAtUtc, nowUtc, window, atRiskThresholdPercent);
    }
}
