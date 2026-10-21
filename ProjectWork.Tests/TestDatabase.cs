using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using ProjectWork.Bookings.Application.Abstractions.Messaging;

namespace ProjectWork.Tests;

// Тестовый хост содержит три НЕЗАВИСИМЫХ контекста, только для проверки границ.
internal sealed class TestDatabase : IDisposable
{
    public ServiceProvider Provider { get; }
    public Guid UserId { get; } = Guid.NewGuid();
    public Guid OtherUserId { get; } = Guid.NewGuid();

    public TestDatabase()
    {
        var services = new ServiceCollection();
        var name = Guid.NewGuid().ToString();
        services.AddLogging();
        services.AddDbContext<ProjectWork.Users.Infrastructure.Persistence.UsersDbContext>(o => o.UseInMemoryDatabase(name + "Users"));
        services.AddScoped<ProjectWork.Users.Application.Abstractions.IUnitOfWork, ProjectWork.Users.Infrastructure.Persistence.UnitOfWork>();
        services.AddScoped<ProjectWork.Users.Application.Abstractions.Repositories.IUserRepository, ProjectWork.Users.Infrastructure.Repositories.UserRepository>();
        ProjectWork.Users.Application.DependencyInjection.AddApplicationServices(services);
        services.AddDbContext<ProjectWork.Events.Infrastructure.Persistence.EventsDbContext>(o => o.UseInMemoryDatabase(name + "Events"));
        services.AddScoped<ProjectWork.Events.Application.Abstractions.IUnitOfWork, ProjectWork.Events.Infrastructure.Persistence.UnitOfWork>();
        services.AddScoped<ProjectWork.Events.Application.Abstractions.Repositories.IEventRepository, ProjectWork.Events.Infrastructure.Repositories.EventRepository>();
        services.AddScoped<ProjectWork.Events.Application.Abstractions.Repositories.IProcessedBookingRepository, ProjectWork.Events.Infrastructure.Repositories.ProcessedBookingRepository>();
        services.AddSingleton<ProjectWork.Events.Application.Abstractions.ICacheService, NoCacheService>();
        services.AddSingleton(new ProjectWork.Events.Application.Common.CacheOptions(
            TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(1)));
        ProjectWork.Events.Application.DependencyInjection.AddApplicationServices(services);
        services.AddDbContext<ProjectWork.Bookings.Infrastructure.Persistence.BookingsDbContext>(o => o.UseInMemoryDatabase(name + "Bookings"));
        services.AddScoped<ProjectWork.Bookings.Application.Abstractions.IUnitOfWork, ProjectWork.Bookings.Infrastructure.Persistence.UnitOfWork>();
        services.AddScoped<ProjectWork.Bookings.Application.Abstractions.Repositories.IBookingRepository, ProjectWork.Bookings.Infrastructure.Repositories.BookingRepository>();
        ProjectWork.Bookings.Application.DependencyInjection.AddApplicationServices(services);
        services.AddSingleton<RecordingBookingEventPublisher>();
        services.AddSingleton<IBookingEventPublisher>(p => p.GetRequiredService<RecordingBookingEventPublisher>());
        ProjectWork.Users.Infrastructure.Security.SecurityRegistration.AddSecurityServices(services,
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Secret"] = "Tests-Only-Secret-At-Least-32-Bytes-Long",
                ["Jwt:Issuer"] = "tests", ["Jwt:Audience"] = "tests", ["Jwt:LifetimeMinutes"] = "60"
            }).Build());
        Provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    public IServiceScope CreateScope() => Provider.CreateScope();
    public void Dispose() => Provider.Dispose();
}
