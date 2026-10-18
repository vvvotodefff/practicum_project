using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ProjectWork.Events.Application.Abstractions;
using ProjectWork.Events.Application.Abstractions.Repositories;
using ProjectWork.Events.Infrastructure.Messaging;
using ProjectWork.Events.Infrastructure.Persistence;
using ProjectWork.Events.Infrastructure.Repositories;

namespace ProjectWork.Events.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructureServices(
        this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("Не задана строка подключения DefaultConnection.");
        services.AddDbContext<EventsDbContext>(options => options.UseNpgsql(connectionString));
        services.AddScoped<IEventRepository, EventRepository>();
        services.AddScoped<IProcessedBookingRepository, ProcessedBookingRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddSingleton(new KafkaOptions(
            configuration["Kafka:BootstrapServers"], configuration["Kafka:ConsumerGroup"]));

        // Порядок важен: топик создаётся до запуска подписчика.
        services.AddHostedService<KafkaTopicInitializer>();
        services.AddHostedService<BookingConfirmedConsumer>();
        return services;
    }
}
