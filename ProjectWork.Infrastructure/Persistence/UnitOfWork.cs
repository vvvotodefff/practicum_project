using Microsoft.EntityFrameworkCore;
using Npgsql;
using ProjectWork.Application.Abstractions;
using ProjectWork.Domain.Exceptions;

namespace ProjectWork.Infrastructure.Persistence;

/// <summary>Сохраняет изменения общего scoped-контекста EF Core одной транзакцией.</summary>
public sealed class UnitOfWork(AppDbContext context) : IUnitOfWork
{
    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "IX_users_login" })
        {
            // Уникальный индекс защищает и от одновременной регистрации одного логина.
            throw new DomainValidationException("Логин уже занят.");
        }
    }
}
