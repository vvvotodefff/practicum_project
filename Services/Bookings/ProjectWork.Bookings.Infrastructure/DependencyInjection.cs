using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ProjectWork.Bookings.Application.Abstractions;
using ProjectWork.Bookings.Application.Abstractions.Repositories;
using ProjectWork.Bookings.Infrastructure.Persistence;
using ProjectWork.Bookings.Infrastructure.Repositories;
using ProjectWork.Bookings.Infrastructure.BackgroundServices;
using ProjectWork.Bookings.Application.Abstractions.Messaging;
using ProjectWork.Bookings.Infrastructure.Messaging;

namespace ProjectWork.Bookings.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructureServices(
        this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("Не задана строка подключения DefaultConnection.");
        services.AddDbContext<BookingsDbContext>(options => options.UseNpgsql(connectionString));
        services.AddScoped<IBookingRepository, BookingRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddSingleton(new KafkaOptions(configuration["Kafka:BootstrapServers"]));
        services.AddSingleton<IBookingEventPublisher, KafkaBookingEventPublisher>();

        services.AddHostedService<BookingProcessingService>();
        return services;
    }
}
