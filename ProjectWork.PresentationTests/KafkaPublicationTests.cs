using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using ProjectWork.Bookings.Application.Abstractions.Messaging;
using ProjectWork.Bookings.Domain.Entities;
using ProjectWork.Bookings.Infrastructure.Messaging;
using ProjectWork.Bookings.Infrastructure.Persistence;
using ProjectWork.Contracts;
using Testcontainers.Kafka;
using Testcontainers.PostgreSql;
using BookingsMarker = ProjectWork.Bookings.Presentation.ApiAssemblyMarker;

namespace ProjectWork.PresentationTests;

public class KafkaPublicationTests
{
    [Fact]
    [Trait("Category", "Kafka")]
    public async Task HttpBooking_BackgroundConfirmation_PublishesCommittedDataWithEventKey()
    {
        // Собственные контейнеры и случайные порты; не используем рабочие БД и localhost:9092.
        await using var postgres = new PostgreSqlBuilder("postgres:16-alpine")
            .WithDatabase("bookings_kafka_test").WithUsername("postgres").WithPassword("postgres").Build();
        await using var kafka = new KafkaBuilder("confluentinc/cp-kafka:7.6.0").WithKRaft().Build();
        using var startupTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await Task.WhenAll(postgres.StartAsync(startupTimeout.Token), kafka.StartAsync(startupTimeout.Token));
        var bootstrapServers = kafka.GetBootstrapAddress();
        using (var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = bootstrapServers }).Build())
        {
            // В приложении создание топика появится на этапе 5. Здесь это подготовка теста.
            await admin.CreateTopicsAsync(
                [new TopicSpecification { Name = KafkaTopics.BookingConfirmed, NumPartitions = 3, ReplicationFactor = 1 }],
                new CreateTopicsOptions { RequestTimeout = TimeSpan.FromSeconds(20) });
        }

        const string secret = "Kafka-Http-Tests-Only-Secret-At-Least-32-Bytes";
        await using var factory = new WebApplicationFactory<BookingsMarker>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<BookingsDbContext>();
                services.RemoveAll<DbContextOptions<BookingsDbContext>>();
                services.AddDbContext<BookingsDbContext>(options => options.UseNpgsql(postgres.GetConnectionString()));
                services.RemoveAll<KafkaOptions>();
                services.AddSingleton(new KafkaOptions(bootstrapServers));
                // Настоящие издатель и фоновый сервис остаются зарегистрированными.
                services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
                {
                    options.TokenValidationParameters.IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
                    options.TokenValidationParameters.ValidIssuer = "kafka-tests";
                    options.TokenValidationParameters.ValidAudience = "kafka-tests";
                });
            });
        });
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        var user = ProjectWork.Users.Domain.Entities.User.Create("kafka-test", "hash");
        var token = new ProjectWork.Users.Infrastructure.Security.JwtTokenGenerator(
            new ProjectWork.Users.Infrastructure.Security.JwtOptions(secret, "kafka-tests", "kafka-tests", 60),
            TimeProvider.System).Generate(user);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using (var first = factory.Services.CreateScope())
        using (var second = factory.Services.CreateScope())
        {
            Assert.Same(first.ServiceProvider.GetRequiredService<IBookingEventPublisher>(),
                second.ServiceProvider.GetRequiredService<IBookingEventPublisher>());
            Assert.IsType<KafkaBookingEventPublisher>(first.ServiceProvider.GetRequiredService<IBookingEventPublisher>());
        }

        using var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = bootstrapServers, GroupId = "stage4-" + Guid.NewGuid(),
            EnableAutoCommit = false, AutoOffsetReset = AutoOffsetReset.Earliest
        }).Build();
        consumer.Assign(Enumerable.Range(0, 3).Select(p =>
            new TopicPartitionOffset(KafkaTopics.BookingConfirmed, new Partition(p), Offset.Beginning)));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var eventId = Guid.NewGuid();
        var ids = new HashSet<Guid>();
        for (var i = 0; i < 2; i++)
        {
            using var response = await client.PostAsync($"/events/{eventId}/book", null, timeout.Token);
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            var booking = await response.Content.ReadFromJsonAsync<JsonElement>(timeout.Token);
            ids.Add(booking.GetProperty("id").GetGuid());
            Assert.Equal("Pending", booking.GetProperty("status").GetString());
        }

        Partition? partition = null;
        for (var i = 0; i < 2; i++)
        {
            // Consume блокирует поток, поэтому выносим его из потока продолжения теста.
            var received = await Task.Run(() => consumer.Consume(timeout.Token), timeout.Token);
            Assert.Equal(KafkaTopics.BookingConfirmed, received.Topic);
            Assert.Equal(eventId.ToString("D"), received.Message.Key);
            if (partition is not null) Assert.Equal(partition.Value, received.Partition);
            partition = received.Partition;
            var message = JsonSerializer.Deserialize<BookingConfirmed>(received.Message.Value)!;
            Assert.True(ids.Remove(message.BookingId), "Получено неожиданное или повторное сообщение");
            Assert.Equal(eventId, message.EventId);
            Assert.Equal(user.Id, message.UserId);
            Assert.Equal(1, message.SeatsCount);
            Assert.Equal(TimeSpan.Zero, message.ConfirmedAt.Offset);
            await using var scope = factory.Services.CreateAsyncScope();
            var saved = await scope.ServiceProvider.GetRequiredService<BookingsDbContext>()
                .Bookings.AsNoTracking().SingleAsync(b => b.Id == message.BookingId, timeout.Token);
            Assert.Equal(BookingStatus.Confirmed, saved.Status);
            // PostgreSQL хранит микросекунды, DateTime — 100 нс.
            Assert.Equal(saved.ProcessedAt!.Value.Ticks / 10, message.ConfirmedAt.UtcDateTime.Ticks / 10);
        }
        Assert.Empty(ids);
        consumer.Close();
    }
}
