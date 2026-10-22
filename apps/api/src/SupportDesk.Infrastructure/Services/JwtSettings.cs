namespace SupportDesk.Infrastructure.Services;

/// <summary>
/// Configuration settings for issuing and validating JWT tokens, bound from the <c>Jwt</c> section.
/// </summary>
public sealed class JwtSettings
{
    public const string SectionName = "Jwt";

    public string SigningKey { get; set; } = string.Empty;

    public string Issuer { get; set; } = "NorthwindSupportDesk";

    public string Audience { get; set; } = "NorthwindSupportDesk.Client";

    /// <summary>Token lifetime in minutes. Defaults to 480 minutes (8 hours, one support shift).</summary>
    public int ExpiryMinutes { get; set; } = 480;
}

