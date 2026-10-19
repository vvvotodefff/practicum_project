using Microsoft.EntityFrameworkCore;
using ProjectWork.Bookings.Application.Abstractions.Repositories;
using ProjectWork.Bookings.Domain.Entities;
using ProjectWork.Bookings.Infrastructure.Persistence;

namespace ProjectWork.Bookings.Infrastructure.Repositories;

/// <summary>Запросы и подготовка изменений броней через scoped-контекст EF Core.</summary>
public sealed class BookingRepository(BookingsDbContext context) : IBookingRepository
{
    /// <inheritdoc />
    public Task<List<Booking>> GetAllAsync(BookingStatus? status = null,
        CancellationToken cancellationToken = default)
    {
        var query = context.Bookings.AsNoTracking();
        if (status.HasValue)
            query = query.Where(b => b.Status == status.Value);
        return query.ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task<Booking?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        context.Bookings.AsNoTracking().SingleOrDefaultAsync(b => b.Id == id, cancellationToken);

    /// <inheritdoc />
    public async Task<Booking?> GetByIdForUpdateAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var booking = await context.Bookings.SingleOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (booking is null) return null;

        await context.Entry(booking).ReloadAsync(cancellationToken);
        return context.Entry(booking).State == EntityState.Detached ? null : booking;
    }

    /// <inheritdoc />
    public Task<int> CountActiveByUserAsync(Guid userId, CancellationToken cancellationToken = default) =>
        context.Bookings.CountAsync(b => b.UserId == userId &&
            (b.Status == BookingStatus.Pending || b.Status == BookingStatus.Confirmed), cancellationToken);

    /// <inheritdoc />
    public Task<List<Guid>> GetIdsByStatusAsync(BookingStatus status,
        CancellationToken cancellationToken = default) =>
        context.Bookings.Where(b => b.Status == status)
            .Select(b => b.Id).ToListAsync(cancellationToken);

    /// <inheritdoc />
    public Task AddAsync(Booking booking, CancellationToken cancellationToken = default)
    {
        context.Bookings.Add(booking);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task UpdateAsync(Booking booking, CancellationToken cancellationToken = default)
    {
        context.Entry(booking).State = EntityState.Modified;
        return Task.CompletedTask;
    }
}
