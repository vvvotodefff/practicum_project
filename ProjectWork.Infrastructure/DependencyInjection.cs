using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ProjectWork.Application.Abstractions;
using ProjectWork.Application.Abstractions.Repositories;
using ProjectWork.Infrastructure.BackgroundServices;
using ProjectWork.Infrastructure.Persistence;
using ProjectWork.Infrastructure.Repositories;
using ProjectWork.Infrastructure.Security;

namespace ProjectWork.Infrastructure;

/// <summary>Регистрация доступа к данным и фоновой обработки бронирований.</summary>
public static class DependencyInjection
{
    /// <summary>Подключить PostgreSQL, scoped-репозитории и фоновый сервис.</summary>
    public static IServiceCollection AddInfrastructureServices(
        this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("Не задана строка подключения DefaultConnection.");

        // Контекст и миграции находятся в одной сборке Infrastructure.
        services.AddSecurityServices(configuration);
        services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IEventRepository, EventRepository>();
        services.AddScoped<IBookingRepository, BookingRepository>();
        services.AddHostedService<BookingProcessingService>();
        return services;
    }
}
