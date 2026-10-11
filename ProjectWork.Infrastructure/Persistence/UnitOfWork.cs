using ProjectWork.Application.Abstractions;

namespace ProjectWork.Infrastructure.Persistence;

/// <summary>Сохраняет изменения общего scoped-контекста EF Core одной транзакцией.</summary>
public sealed class UnitOfWork(AppDbContext context) : IUnitOfWork
{
    /// <inheritdoc />
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        context.SaveChangesAsync(cancellationToken);
}
