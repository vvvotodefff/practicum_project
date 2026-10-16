using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ProjectWork.Application.Abstractions;
using ProjectWork.Application.Abstractions.Repositories;
using ProjectWork.Infrastructure;
using ProjectWork.Infrastructure.BackgroundServices;
using ProjectWork.Infrastructure.Persistence;
using ProjectWork.Infrastructure.Repositories;

namespace ProjectWork.Tests;

public class InfrastructureRegistrationTests
{
    // Тесты проверяют регистрацию и метаданные, соединение с PostgreSQL не открывают.
    private const string ConnectionString =
        "Host=localhost;Port=1;Database=registration_test;Username=test;Password=test";

    private static IConfiguration Configuration(string? connectionString = ConnectionString) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = connectionString,
            ["Jwt:Secret"] = "Test-Only-Signing-Key-At-Least-32-Bytes",
            ["Jwt:Issuer"] = "TestIssuer",
            ["Jwt:Audience"] = "TestAudience",
            ["Jwt:LifetimeMinutes"] = "60"
        }).Build();

    private static ServiceProvider BuildProvider(IServiceCollection services) =>
        services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });

    [Fact]
    public void AddInfrastructureServices_RegistersScopedContextAndRepositories()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        Assert.Same(services, services.AddInfrastructureServices(Configuration()));
        foreach (var serviceType in new[] { typeof(AppDbContext), typeof(IEventRepository), typeof(IBookingRepository), typeof(IUnitOfWork) })
            Assert.Equal(ServiceLifetime.Scoped,
                Assert.Single(services, descriptor => descriptor.ServiceType == serviceType).Lifetime);

        using var provider = BuildProvider(services);
        using var first = provider.CreateScope();
        using var second = provider.CreateScope();
        var context = first.ServiceProvider.GetRequiredService<AppDbContext>();
        var events = first.ServiceProvider.GetRequiredService<IEventRepository>();
        var bookings = first.ServiceProvider.GetRequiredService<IBookingRepository>();

        Assert.IsType<EventRepository>(events);
        Assert.IsType<BookingRepository>(bookings);
        Assert.IsType<UnitOfWork>(first.ServiceProvider.GetRequiredService<IUnitOfWork>());
        Assert.Same(first.ServiceProvider.GetRequiredService<IUnitOfWork>(), first.ServiceProvider.GetRequiredService<IUnitOfWork>());
        Assert.NotSame(first.ServiceProvider.GetRequiredService<IUnitOfWork>(), second.ServiceProvider.GetRequiredService<IUnitOfWork>());
        Assert.Same(context, first.ServiceProvider.GetRequiredService<AppDbContext>());
        Assert.Same(events, first.ServiceProvider.GetRequiredService<IEventRepository>());
        Assert.Same(bookings, first.ServiceProvider.GetRequiredService<IBookingRepository>());
        Assert.NotSame(context, second.ServiceProvider.GetRequiredService<AppDbContext>());
        Assert.NotSame(events, second.ServiceProvider.GetRequiredService<IEventRepository>());
        Assert.NotSame(bookings, second.ServiceProvider.GetRequiredService<IBookingRepository>());
    }

    [Fact]
    public void AddInfrastructureServices_ConfiguresPostgresAndFindsExistingMigrationInInfrastructure()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructureServices(Configuration());
        using var provider = BuildProvider(services);
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        Assert.Equal("Npgsql.EntityFrameworkCore.PostgreSQL", context.Database.ProviderName);
        Assert.Equal(ConnectionString, context.Database.GetConnectionString());
        Assert.Equal(typeof(AppDbContext).Assembly, context.GetService<IMigrationsAssembly>().Assembly);
        Assert.Equal(2, context.Database.GetMigrations().Count());
        Assert.Contains("20261006154025_InitialCreate", context.Database.GetMigrations());
        Assert.False(context.Database.HasPendingModelChanges());
    }

    [Fact]
    public void AddInfrastructureServices_RegistersOneSingletonBookingWorker()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructureServices(Configuration());
        using var provider = BuildProvider(services);

        var worker = Assert.Single(provider.GetServices<IHostedService>());

        Assert.IsType<BookingProcessingService>(worker);
        Assert.Same(worker, Assert.Single(provider.GetServices<IHostedService>()));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void AddInfrastructureServices_RejectsMissingOrBlankConnectionString(string? connectionString)
    {
        var services = new ServiceCollection();

        var error = Assert.Throws<InvalidOperationException>(() =>
            services.AddInfrastructureServices(Configuration(connectionString)));

        Assert.Contains("DefaultConnection", error.Message);
        Assert.Empty(services);
    }
}
