using Microsoft.Extensions.DependencyInjection;
using ProjectWork.Bookings.Application.Services;

namespace ProjectWork.Bookings.Application;

/// <summary>Регистрация прикладных сервисов.</summary>
public static class DependencyInjection
{
    /// <summary>Подключить прикладной слой.</summary>
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddScoped<IBookingService, BookingService>();
        return services;
    }
}
