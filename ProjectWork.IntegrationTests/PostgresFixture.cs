using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;

namespace ProjectWork.IntegrationTests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "PostgreSQL integration";
}

public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("postgres").WithUsername("postgres").WithPassword("postgres").Build();
    private ServiceProvider? _provider;

    public async Task InitializeAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await _container.StartAsync(timeout.Token);
        var services = new ServiceCollection();
        services.AddDbContext<ProjectWork.Users.Infrastructure.Persistence.UsersDbContext>(options =>
            options.UseNpgsql(new NpgsqlConnectionStringBuilder(_container.GetConnectionString()) { Database = "users_tests" }.ConnectionString));
        services.AddScoped<ProjectWork.Users.Application.Abstractions.IUnitOfWork, ProjectWork.Users.Infrastructure.Persistence.UnitOfWork>();
        services.AddScoped<ProjectWork.Users.Application.Abstractions.Repositories.IUserRepository, ProjectWork.Users.Infrastructure.Repositories.UserRepository>();
        services.AddDbContext<ProjectWork.Events.Infrastructure.Persistence.EventsDbContext>(options =>
            options.UseNpgsql(new NpgsqlConnectionStringBuilder(_container.GetConnectionString()) { Database = "events_tests" }.ConnectionString));
        services.AddScoped<ProjectWork.Events.Application.Abstractions.IUnitOfWork, ProjectWork.Events.Infrastructure.Persistence.UnitOfWork>();
        services.AddScoped<ProjectWork.Events.Application.Abstractions.Repositories.IEventRepository, ProjectWork.Events.Infrastructure.Repositories.EventRepository>();
        services.AddDbContext<ProjectWork.Bookings.Infrastructure.Persistence.BookingsDbContext>(options =>
            options.UseNpgsql(new NpgsqlConnectionStringBuilder(_container.GetConnectionString()) { Database = "bookings_tests" }.ConnectionString));
        services.AddScoped<ProjectWork.Bookings.Application.Abstractions.IUnitOfWork, ProjectWork.Bookings.Infrastructure.Persistence.UnitOfWork>();
        services.AddScoped<ProjectWork.Bookings.Application.Abstractions.Repositories.IBookingRepository, ProjectWork.Bookings.Infrastructure.Repositories.BookingRepository>();
        _provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    public AsyncServiceScope CreateScope() => _provider!.CreateAsyncScope();

    public async Task ResetAsync<TContext>() where TContext : DbContext
    {
        await using var scope = CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TContext>();
        // Только собственный временный контейнер, никогда не настройки рабочего приложения.
        await context.Database.EnsureDeletedAsync();
        await context.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        try { if (_provider is not null) await _provider.DisposeAsync(); }
        finally { await _container.DisposeAsync(); }
    }
}
