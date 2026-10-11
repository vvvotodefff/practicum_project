using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectWork.Application.Abstractions;
using ProjectWork.Application.Abstractions.Repositories;
using ProjectWork.Infrastructure.Persistence;
using ProjectWork.Infrastructure.Repositories;
using Testcontainers.PostgreSql;

namespace ProjectWork.IntegrationTests;

// Одна коллекция = один PostgreSQL на все классы тестов.
// Тесты не должны одновременно сбрасывать общую базу.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "PostgreSQL integration";
}

public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("eventapi_integration")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    private ServiceProvider? _provider;

    public async Task InitializeAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await _container.StartAsync(timeout.Token);

        // Только адрес собственного контейнера: никаких appsettings и порта 5433.
        var connectionString = _container.GetConnectionString();
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IEventRepository, EventRepository>();
        services.AddScoped<IBookingRepository, BookingRepository>();
        _provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });
    }

    public AsyncServiceScope CreateScope() =>
        (_provider ?? throw new InvalidOperationException("PostgreSQL fixture is not initialized."))
        .CreateAsyncScope();

    public async Task ResetDatabaseAsync()
    {
        await using var scope = CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        // Удаляется только БД временного контейнера. Создание схемы — только миграциями.
        await context.Database.EnsureDeletedAsync();
        await context.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        try
        {
            if (_provider is not null)
                await _provider.DisposeAsync();
        }
        finally
        {
            await _container.DisposeAsync();
        }
    }
}
