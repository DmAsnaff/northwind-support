using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SupportDesk.Application.Abstractions;
using SupportDesk.Domain.Aggregates.Agents;

namespace SupportDesk.Infrastructure.Services;

/// <summary>
/// Issues and validates standard RFC 7519 HS256 JWT tokens using a configured symmetric key.
/// </summary>
public sealed class JwtTokenService : ITokenService
{
    private static readonly byte[] HeaderSegmentBytes =
        Encoding.UTF8.GetBytes(Base64UrlEncode(Encoding.UTF8.GetBytes("""{"alg":"HS256","typ":"JWT"}""")));

    private readonly JwtSettings _settings;
    private readonly byte[] _keyBytes;

    public JwtTokenService(JwtSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (string.IsNullOrWhiteSpace(settings.SigningKey) || Encoding.UTF8.GetByteCount(settings.SigningKey) < 32)
        {
            throw new InvalidOperationException(
                "Jwt:SigningKey must be configured and at least 32 bytes long. Set Jwt__SigningKey in environment variables or appsettings.Development.json.");
        }

        _settings = settings;
        _keyBytes = Encoding.UTF8.GetBytes(settings.SigningKey);
    }

    public IssuedToken IssueToken(Agent agent, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(agent);

        var issuedAt = new DateTimeOffset(DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc));
        var expiresAtUtc = nowUtc.AddMinutes(_settings.ExpiryMinutes);
        var expiresAt = new DateTimeOffset(DateTime.SpecifyKind(expiresAtUtc, DateTimeKind.Utc));

        var payload = new JwtPayloadModel(
            Sub: agent.Id.ToString(CultureInfo.InvariantCulture),
            Name: agent.FullName,
            Email: agent.Email,
            Iss: _settings.Issuer,
            Aud: _settings.Audience,
            Iat: issuedAt.ToUnixTimeSeconds(),
            Exp: expiresAt.ToUnixTimeSeconds());

        var headerSegment = Encoding.UTF8.GetString(HeaderSegmentBytes);
        var payloadJson = JsonSerializer.SerializeToUtf8Bytes(payload, JwtJsonOptions);
        var payloadSegment = Base64UrlEncode(payloadJson);

        var signingInput = $"{headerSegment}.{payloadSegment}";
        var signatureBytes = ComputeSignature(signingInput);
        var signatureSegment = Base64UrlEncode(signatureBytes);

        return new IssuedToken($"{signingInput}.{signatureSegment}", expiresAtUtc);
    }

    public ClaimsPrincipal? ValidateToken(string token, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var parts = token.Split('.');
        if (parts.Length != 3)
        {
            return null;
        }

        var signingInput = $"{parts[0]}.{parts[1]}";
        byte[] providedSignature;
        try
        {
            providedSignature = Base64UrlDecode(parts[2]);
        }
        catch (FormatException)
        {
            return null;
        }

        var expectedSignature = ComputeSignature(signingInput);
        if (!CryptographicOperations.FixedTimeEquals(providedSignature, expectedSignature))
        {
            return null;
        }

        JwtPayloadModel? payload;
        try
        {
            var payloadBytes = Base64UrlDecode(parts[1]);
            payload = JsonSerializer.Deserialize<JwtPayloadModel>(payloadBytes, JwtJsonOptions);
        }
        catch
        {
            return null;
        }

        if (payload is null
            || string.IsNullOrWhiteSpace(payload.Sub)
            || !string.Equals(payload.Iss, _settings.Issuer, StringComparison.Ordinal)
            || !string.Equals(payload.Aud, _settings.Audience, StringComparison.Ordinal))
        {
            return null;
        }

        var nowUnix = new DateTimeOffset(DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc)).ToUnixTimeSeconds();
        if (nowUnix >= payload.Exp)
        {
            return null;
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, payload.Sub),
            new("sub", payload.Sub),
            new(ClaimTypes.Name, payload.Name ?? string.Empty),
            new(ClaimTypes.Email, payload.Email ?? string.Empty)
        };

        var identity = new ClaimsIdentity(claims, "Bearer");
        return new ClaimsPrincipal(identity);
    }

    private byte[] ComputeSignature(string signingInput)
    {
        using var hmac = new HMACSHA256(_keyBytes);
        return hmac.ComputeHash(Encoding.UTF8.GetBytes(signingInput));
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static byte[] Base64UrlDecode(string input)
    {
        var base64 = input.Replace('-', '+').Replace('_', '/');
        switch (base64.Length % 4)
        {
            case 2:
                base64 += "==";
                break;
            case 3:
                base64 += "=";
                break;
        }

        return Convert.FromBase64String(base64);
    }

    private static readonly JsonSerializerOptions JwtJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private sealed record JwtPayloadModel(
        string Sub,
        string Name,
        string Email,
        string Iss,
        string Aud,
        long Iat,
        long Exp);
}

