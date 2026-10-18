using Microsoft.Extensions.DependencyInjection;
using ProjectWork.Bookings.Application.Abstractions;
using ProjectWork.Bookings.Application.Abstractions.Repositories;
using ProjectWork.Bookings.Domain.Entities;
using ProjectWork.Bookings.Infrastructure.Persistence;

namespace ProjectWork.IntegrationTests;

public abstract class BookingsDatabaseTest(PostgresFixture fixture) : DatabaseTest<BookingsDbContext>(fixture)
{
    protected IUnitOfWork UnitOfWork => Services.GetRequiredService<IUnitOfWork>();
    protected IBookingRepository Bookings => Services.GetRequiredService<IBookingRepository>();

    protected Guid UserId { get; } = Guid.NewGuid();
    protected Booking NewBooking(Guid eventId, BookingStatus status = BookingStatus.Pending)
    {
        var booking = Booking.Create(eventId, UserId);
        switch (status)
        {
            case BookingStatus.Confirmed: booking.Confirm(); break;
            case BookingStatus.Rejected: booking.Reject(); break;
            case BookingStatus.Cancelled: booking.Cancel(); break;
        }
        Context.Entry(booking).Property(b => b.CreatedAt).CurrentValue = At(1);
        Context.Entry(booking).Property(b => b.ProcessedAt).CurrentValue = status == BookingStatus.Pending ? null : At(1, 11);
        return booking;
    }
}
