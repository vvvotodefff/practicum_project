using Microsoft.Extensions.DependencyInjection;
using ProjectWork.Users.Application.Services;

namespace ProjectWork.Users.Application;

/// <summary>Регистрация прикладных сервисов.</summary>
public static class DependencyInjection
{
    /// <summary>Подключить прикладной слой.</summary>
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddScoped<IUserService, UserService>();
        return services;
    }
}
