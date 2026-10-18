using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ProjectWork.Users.Application.Abstractions.Security;

namespace ProjectWork.Users.Infrastructure.Security;

/// <summary>Регистрация независимых компонентов безопасности.</summary>
public static class SecurityRegistration
{
    public static IServiceCollection AddSecurityServices(
        this IServiceCollection services, IConfiguration configuration)
    {
        // Проверяем конфигурацию при запуске, до первой попытки выдать токен.
        services.AddSingleton(JwtOptions.FromConfiguration(configuration));
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        services.AddSingleton<IPasswordHasher, Sha256PasswordHasher>();
        services.AddSingleton<IJwtTokenGenerator, JwtTokenGenerator>();
        return services;
    }
}
