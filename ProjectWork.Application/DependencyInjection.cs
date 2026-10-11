using Microsoft.Extensions.DependencyInjection;
using ProjectWork.Application.Services;

namespace ProjectWork.Application;

/// <summary>Регистрация прикладных сервисов без выбора реализации доступа к данным.</summary>
public static class DependencyInjection
{
    /// <summary>Добавить scoped-сервисы событий и бронирований.</summary>
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddScoped<IEventService, EventService>();
        services.AddScoped<IBookingService, BookingService>();
        return services;
    }
}
