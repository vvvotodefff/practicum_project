using System.Text;
using Microsoft.Extensions.Configuration;

namespace ProjectWork.Events.Infrastructure.Security;

/// <summary>
/// Проверенные параметры проверки JWT, выпущенного сервисом Users.
/// Секрет, издатель и аудитория должны совпадать с настройками Users.
/// </summary>
public sealed class JwtOptions
{
    public string Secret { get; }
    public string Issuer { get; }
    public string Audience { get; }

    public JwtOptions(string secret, string issuer, string audience)
    {
        if (string.IsNullOrWhiteSpace(secret) || Encoding.UTF8.GetByteCount(secret) < 32)
            throw new InvalidOperationException("Jwt:Secret должен содержать не менее 32 байт UTF-8.");
        if (string.IsNullOrWhiteSpace(issuer))
            throw new InvalidOperationException("Не задан Jwt:Issuer.");
        if (string.IsNullOrWhiteSpace(audience))
            throw new InvalidOperationException("Не задан Jwt:Audience.");

        Secret = secret;
        Issuer = issuer;
        Audience = audience;
    }

    public static JwtOptions FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return new JwtOptions(configuration["Jwt:Secret"]!, configuration["Jwt:Issuer"]!,
            configuration["Jwt:Audience"]!);
    }
}
