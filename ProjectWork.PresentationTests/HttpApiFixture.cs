using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using System.Text;
using Testcontainers.PostgreSql;
using UsersMarker = ProjectWork.Users.Presentation.ApiAssemblyMarker;
using EventsMarker = ProjectWork.Events.Presentation.ApiAssemblyMarker;
using BookingsMarker = ProjectWork.Bookings.Presentation.ApiAssemblyMarker;
using ProjectWork.Users.Infrastructure.Persistence;
using ProjectWork.Events.Infrastructure.Persistence;
using ProjectWork.Bookings.Infrastructure.Persistence;
using ProjectWork.Bookings.Application.Abstractions.Messaging;
using ProjectWork.Events.Infrastructure.Messaging;
using Microsoft.Extensions.Hosting;

namespace ProjectWork.PresentationTests;

[CollectionDefinition("HTTP API", DisableParallelization = true)]
public sealed class HttpApiCollection : ICollectionFixture<HttpApiFixture>;

public sealed class HttpApiFixture : IAsyncLifetime
{
    public const string Secret = "Http-Tests-Only-Secret-At-Least-32-Bytes";
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("postgres").WithUsername("postgres").WithPassword("postgres").Build();
    private WebApplicationFactory<UsersMarker>? _users;
    private WebApplicationFactory<EventsMarker>? _events;
    private WebApplicationFactory<BookingsMarker>? _bookings;

    public async Task InitializeAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await _postgres.StartAsync(timeout.Token);
        _users = Factory<UsersMarker, UsersDbContext>("users_http");
        _events = Factory<EventsMarker, EventsDbContext>("events_http");
        _bookings = Factory<BookingsMarker, BookingsDbContext>("bookings_http");
        // Три настоящих приложения с независимыми БД и HTTP-конвейерами.
        using var users = Users();
        using var events = Events();
        using var bookings = Bookings();
        (await users.GetAsync("/swagger/v1/swagger.json")).EnsureSuccessStatusCode();
        (await events.GetAsync("/events")).EnsureSuccessStatusCode();
        (await bookings.GetAsync("/swagger/v1/swagger.json")).EnsureSuccessStatusCode();
    }

    private WebApplicationFactory<TEntry> Factory<TEntry, TContext>(string database)
        where TEntry : class where TContext : DbContext =>
        new WebApplicationFactory<TEntry>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<TContext>();
                services.RemoveAll<DbContextOptions<TContext>>();
                services.AddDbContext<TContext>(options => options.UseNpgsql(
                    new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString()) { Database = database }.ConnectionString));
                if (typeof(TEntry) == typeof(UsersMarker))
                {
                    services.RemoveAll<ProjectWork.Users.Infrastructure.Security.JwtOptions>();
                    services.AddSingleton(new ProjectWork.Users.Infrastructure.Security.JwtOptions(Secret, "http-tests", "http-tests", 60));
                }
                if (typeof(TEntry) == typeof(EventsMarker))
                {
                    // HTTP-тестам брокер не нужен: подписчик и создание топика проверяются отдельно.
                    foreach (var kafkaService in services.Where(d => d.ServiceType == typeof(IHostedService) &&
                        (d.ImplementationType == typeof(KafkaTopicInitializer) ||
                         d.ImplementationType == typeof(BookingConfirmedConsumer))).ToList())
                        services.Remove(kafkaService);
                }
                if (typeof(TEntry) == typeof(BookingsMarker))
                {
                    services.RemoveAll<IBookingEventPublisher>();
                    services.AddSingleton<IBookingEventPublisher, RecordingBookingEventPublisher>();
                }
                services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
                {
                    options.TokenValidationParameters.IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Secret));
                    options.TokenValidationParameters.ValidIssuer = "http-tests";
                    options.TokenValidationParameters.ValidAudience = "http-tests";
                });
            });
        });

    private static HttpClient Client<T>(WebApplicationFactory<T> factory) where T : class =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
    public HttpClient Users() => Client(_users!);
    public HttpClient Events() => Client(_events!);
    public HttpClient Bookings() => Client(_bookings!);

    public async Task DisposeAsync()
    {
        try
        {
            if (_bookings is not null) await _bookings.DisposeAsync();
            if (_events is not null) await _events.DisposeAsync();
            if (_users is not null) await _users.DisposeAsync();
        }
        finally { await _postgres.DisposeAsync(); }
    }
}
