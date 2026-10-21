using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ProjectWork.Events.Application.Abstractions;
using ProjectWork.Events.Application.Abstractions.Repositories;
using ProjectWork.Events.Application.Common;
using ProjectWork.Events.Infrastructure.Caching;
using ProjectWork.Events.Infrastructure.Messaging;
using ProjectWork.Events.Infrastructure.Persistence;
using ProjectWork.Events.Infrastructure.Repositories;
using StackExchange.Redis;

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

        var redisConnectionString = configuration["Redis:ConnectionString"];
        if (string.IsNullOrWhiteSpace(redisConnectionString))
            throw new InvalidOperationException("Не задана строка подключения Redis:ConnectionString.");
        var redisOptions = ConfigurationOptions.Parse(redisConnectionString);
        // Сервис должен стартовать и без Redis: клиент переподключится в фоне,
        // а пока кеша нет, запросы идут напрямую в базу.
        redisOptions.AbortOnConnectFail = false;
        // Без соединения команды сразу завершаются ошибкой, а не ждут в очереди до таймаута:
        // иначе каждый запрос при упавшем Redis тормозил бы на секунды перед походом в базу.
        redisOptions.BacklogPolicy = BacklogPolicy.FailFast;

        // Клиент Redis тяжёлый и потокобезопасный — одно соединение на всё приложение.
        services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisOptions));
        services.AddSingleton<ICacheService, RedisCacheService>();
        services.AddSingleton(new CacheOptions(
            configuration.GetValue("Redis:EventTtl", TimeSpan.FromMinutes(5)),
            configuration.GetValue("Redis:TopEventsTtl", TimeSpan.FromMinutes(1))));
        return services;
    }
}
