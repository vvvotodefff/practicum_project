using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectWork.Events.Infrastructure.Persistence;

namespace ProjectWork.Events.Infrastructure;

/// <summary>Инициализация схемы базы перед запуском HTTP и фоновых задач.</summary>
public static class DatabaseInitialization
{
    /// <summary>Применить существующие миграции в отдельном scope, сохранив данные.</summary>
    public static async Task MigrateDatabaseAsync(this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<EventsDbContext>();
        await context.Database.MigrateAsync(cancellationToken);
    }
}
