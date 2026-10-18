using ProjectWork.Events.Application.Abstractions;

namespace ProjectWork.Events.Infrastructure.Persistence;

public sealed class UnitOfWork(EventsDbContext context) : IUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        context.SaveChangesAsync(cancellationToken);
}
