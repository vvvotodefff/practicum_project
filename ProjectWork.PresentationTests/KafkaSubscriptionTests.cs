using System.Text.Json;
using Confluent.Kafka;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ProjectWork.Contracts;
using ProjectWork.Events.Domain.Entities;
using ProjectWork.Events.Infrastructure.Messaging;
using ProjectWork.Events.Infrastructure.Persistence;
using Testcontainers.Kafka;
using Testcontainers.PostgreSql;
using EventsMarker = ProjectWork.Events.Presentation.ApiAssemblyMarker;

namespace ProjectWork.PresentationTests;

public class KafkaSubscriptionTests
{
    [Fact]
    [Trait("Category", "Kafka")]
    public async Task BookingConfirmed_OnEmptyBroker_CreatesTopicAndReservesSeatsOncePerBooking()
    {
        // Собственные контейнеры; топик заранее НЕ создаём — это делает сервис Events.
        await using var postgres = new PostgreSqlBuilder("postgres:16-alpine")
            .WithDatabase("events_kafka_test").WithUsername("postgres").WithPassword("postgres").Build();
        await using var kafka = new KafkaBuilder("confluentinc/cp-kafka:7.6.0").WithKRaft().Build();
        using var startupTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await Task.WhenAll(postgres.StartAsync(startupTimeout.Token), kafka.StartAsync(startupTimeout.Token));
        var bootstrapServers = kafka.GetBootstrapAddress();

        await using var factory = new WebApplicationFactory<EventsMarker>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<EventsDbContext>();
                services.RemoveAll<DbContextOptions<EventsDbContext>>();
                services.AddDbContext<EventsDbContext>(options => options.UseNpgsql(postgres.GetConnectionString()));
                services.RemoveAll<KafkaOptions>();
                services.AddSingleton(new KafkaOptions(bootstrapServers, "events-tests"));
            });
        });
        // Запуск хоста: миграции, создание топика и подписчик.
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

        var eventItem = Event.Create("Kafka", null, DateTime.UtcNow.AddDays(1), DateTime.UtcNow.AddDays(1).AddHours(1), 5);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<EventsDbContext>();
            context.Events.Add(eventItem);
            await context.SaveChangesAsync();
        }

        var first = new BookingConfirmed(Guid.NewGuid(), eventItem.Id, Guid.NewGuid(), 2, DateTimeOffset.UtcNow);
        var second = first with { BookingId = Guid.NewGuid(), SeatsCount = 1 };
        using (var producer = new ProducerBuilder<string, string>(new ProducerConfig { BootstrapServers = bootstrapServers }).Build())
        {
            // Дубликат первой брони и некорректное сообщение не должны менять итог.
            foreach (var value in new[]
                     {
                         JsonSerializer.Serialize(first), JsonSerializer.Serialize(first),
                         "not-json", JsonSerializer.Serialize(second)
                     })
            {
                await producer.ProduceAsync(KafkaTopics.BookingConfirmed,
                    new Message<string, string> { Key = eventItem.Id.ToString("D"), Value = value });
            }
        }

        // Один ключ — один раздел: когда применена вторая бронь, дубликат уже обработан.
        int availableSeats;
        var deadline = DateTime.UtcNow.AddMinutes(1);
        do
        {
            await Task.Delay(500);
            await using var scope = factory.Services.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<EventsDbContext>();
            var done = await context.ProcessedBookings.AnyAsync(p => p.BookingId == second.BookingId);
            availableSeats = await context.Events.Where(e => e.Id == eventItem.Id)
                .Select(e => e.AvailableSeats).SingleAsync();
            if (done) break;
        } while (DateTime.UtcNow < deadline);

        Assert.Equal(2, availableSeats);
    }
}
