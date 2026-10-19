namespace ProjectWork.Users.Application.Abstractions;

/// <summary>Атомарное сохранение изменений всех репозиториев текущего scope.</summary>
public interface IUnitOfWork
{
    /// <summary>Сохранить подготовленные изменения одной операцией.</summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
