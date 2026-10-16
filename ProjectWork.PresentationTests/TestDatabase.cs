using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectWork.Application;
using ProjectWork.Domain.Entities;
using ProjectWork.Infrastructure.Security;
using Microsoft.Extensions.Configuration;
using ProjectWork.Application.Abstractions;
using ProjectWork.Application.Abstractions.Repositories;
using ProjectWork.Infrastructure.Persistence;
using ProjectWork.Infrastructure.Repositories;

namespace ProjectWork.PresentationTests;

// Отдельная InMemory-база для проверки контроллеров без запуска PostgreSQL.
internal sealed class TestDatabase : IDisposable
{
    private readonly ServiceProvider _provider;

    public Guid UserId { get; private set; }
    public Guid OtherUserId { get; private set; }

    private sealed class TestClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    }

    public TestDatabase()
    {
        var databaseName = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(new TestClock());
        services.AddSecurityServices(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Secret"] = "Test-Only-Secret-At-Least-32-Bytes-Long",
            ["Jwt:Issuer"] = "tests", ["Jwt:Audience"] = "tests", ["Jwt:LifetimeMinutes"] = "60"
        }).Build());
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase(databaseName));
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IEventRepository, EventRepository>();
        services.AddScoped<IBookingRepository, BookingRepository>();
        services.AddApplicationServices();
        _provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });
        SeedUsers();
    }

    private void SeedUsers()
    {
        using var scope = CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hasher = new Sha256PasswordHasher();
        var first = User.Create("test-user", hasher.Hash("password"));
        var second = User.Create("other-user", hasher.Hash("password"));
        context.Users.AddRange(first, second);
        context.SaveChanges();
        UserId = first.Id;
        OtherUserId = second.Id;
    }

    public IServiceScope CreateScope() => _provider.CreateScope();
    public void Dispose() => _provider.Dispose();
}
