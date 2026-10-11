using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectWork.Infrastructure.Persistence;

namespace ProjectWork.Infrastructure;

/// <summary>Инициализация схемы базы перед запуском HTTP и фоновых задач.</summary>
public static class DatabaseInitialization
{
    /// <summary>Применить существующие миграции в отдельном scope, сохранив данные.</summary>
    public static async Task MigrateDatabaseAsync(this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await context.Database.MigrateAsync(cancellationToken);
    }
}
