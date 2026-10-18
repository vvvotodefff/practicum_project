using Microsoft.Extensions.DependencyInjection;
using ProjectWork.Events.Application.Services;

namespace ProjectWork.Events.Application;

/// <summary>Регистрация прикладных сервисов.</summary>
public static class DependencyInjection
{
    /// <summary>Подключить прикладной слой.</summary>
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddScoped<IEventService, EventService>();
        return services;
    }
}
