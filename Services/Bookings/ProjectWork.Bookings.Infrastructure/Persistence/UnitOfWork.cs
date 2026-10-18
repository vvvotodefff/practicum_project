using ProjectWork.Bookings.Application.Abstractions;

namespace ProjectWork.Bookings.Infrastructure.Persistence;

public sealed class UnitOfWork(BookingsDbContext context) : IUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        context.SaveChangesAsync(cancellationToken);
}
