using System.Globalization;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace ProjectWork.Infrastructure.Security;

/// <summary>Проверенные настройки подписания и времени жизни JWT.</summary>
public sealed class JwtOptions
{
    public string Secret { get; }
    public string Issuer { get; }
    public string Audience { get; }
    public int LifetimeMinutes { get; }

    public JwtOptions(string secret, string issuer, string audience, int lifetimeMinutes)
    {
        if (string.IsNullOrWhiteSpace(secret) || Encoding.UTF8.GetByteCount(secret) < 32)
            throw new InvalidOperationException("Jwt:Secret должен содержать не менее 32 байт UTF-8.");
        if (string.IsNullOrWhiteSpace(issuer))
            throw new InvalidOperationException("Не задан Jwt:Issuer.");
        if (string.IsNullOrWhiteSpace(audience))
            throw new InvalidOperationException("Не задан Jwt:Audience.");
        if (lifetimeMinutes <= 0)
            throw new InvalidOperationException("Jwt:LifetimeMinutes должен быть положительным числом.");

        Secret = secret;
        Issuer = issuer;
        Audience = audience;
        LifetimeMinutes = lifetimeMinutes;
    }

    public static JwtOptions FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (!int.TryParse(configuration["Jwt:LifetimeMinutes"], NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var lifetime))
            throw new InvalidOperationException("Jwt:LifetimeMinutes должен быть целым числом.");

        return new JwtOptions(configuration["Jwt:Secret"]!, configuration["Jwt:Issuer"]!,
            configuration["Jwt:Audience"]!, lifetime);
    }
}
