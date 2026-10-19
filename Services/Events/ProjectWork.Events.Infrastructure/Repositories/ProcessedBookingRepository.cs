using Microsoft.EntityFrameworkCore;
using ProjectWork.Events.Application.Abstractions.Repositories;
using ProjectWork.Events.Infrastructure.Persistence;

namespace ProjectWork.Events.Infrastructure.Repositories;

/// <summary>Учёт обработанных подтверждений броней через EF Core.</summary>
public sealed class ProcessedBookingRepository(EventsDbContext context)
    : IProcessedBookingRepository
{
    /// <inheritdoc />
    public Task<bool> ExistsAsync(Guid bookingId, CancellationToken cancellationToken = default) =>
        context.ProcessedBookings.AsNoTracking().AnyAsync(p => p.BookingId == bookingId, cancellationToken);

    /// <inheritdoc />
    public Task AddAsync(Guid bookingId, CancellationToken cancellationToken = default)
    {
        context.ProcessedBookings.Add(new ProcessedBooking
        {
            BookingId = bookingId, ProcessedAt = DateTime.UtcNow
        });
        return Task.CompletedTask;
    }
}
