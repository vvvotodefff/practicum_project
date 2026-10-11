using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectWork.Application;
using ProjectWork.Application.Abstractions;
using ProjectWork.Application.Abstractions.Repositories;
using ProjectWork.Infrastructure.Persistence;
using ProjectWork.Infrastructure.Repositories;

namespace ProjectWork.PresentationTests;

// Отдельная InMemory-база для проверки контроллеров без запуска PostgreSQL.
internal sealed class TestDatabase : IDisposable
{
    private readonly ServiceProvider _provider;

    public TestDatabase()
    {
        var databaseName = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddLogging();
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
    }

    public IServiceScope CreateScope() => _provider.CreateScope();
    public void Dispose() => _provider.Dispose();
}
