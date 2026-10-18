using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectWork.Bookings.Application.Services;
using ProjectWork.Bookings.Domain.Entities;
using ProjectWork.Bookings.Domain.Exceptions;
using ProjectWork.Bookings.Infrastructure.Persistence;

namespace ProjectWork.Tests;

public class BookingServiceTests
{
    [Fact]
    public async Task Create_StoresExternalIdsWithoutOtherDatabases()
    {
        using var db = new TestDatabase();
        using var scope = db.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IBookingService>();
        var eventId = Guid.NewGuid();
        var booking = await service.CreateBookingAsync(eventId, db.UserId);
        Assert.Equal(eventId, booking.EventId);
        Assert.Equal(db.UserId, booking.UserId);
        Assert.Equal(BookingStatus.Pending, booking.Status);
        Assert.Null(booking.ProcessedAt);
        Assert.Single(await scope.ServiceProvider.GetRequiredService<BookingsDbContext>().Bookings.ToListAsync());
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<ProjectWork.Users.Infrastructure.Persistence.UsersDbContext>().Users.ToListAsync());
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<ProjectWork.Events.Infrastructure.Persistence.EventsDbContext>().Events.ToListAsync());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task ReadAndCancel_CheckOwnerOrAdmin(bool owner, bool admin)
    {
        using var db = new TestDatabase();
        using var scope = db.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IBookingService>();
        var booking = await service.CreateBookingAsync(Guid.NewGuid(), db.UserId);
        var caller = owner ? db.UserId : db.OtherUserId;
        var read = await service.GetBookingByIdAsync(booking.Id, caller, admin);
        Assert.Equal(owner || admin, read is not null);
        Assert.Null(await service.GetBookingByIdAsync(Guid.NewGuid(), caller, admin));
        if (!owner && !admin)
        {
            await Assert.ThrowsAsync<ForbiddenOperationException>(() => service.CancelBookingAsync(booking.Id, caller, admin));
            Assert.Equal(BookingStatus.Pending, (await service.GetBookingByIdAsync(booking.Id, db.UserId, false))!.Status);
        }
        else
        {
            await service.CancelBookingAsync(booking.Id, caller, admin);
            await service.CancelBookingAsync(booking.Id, caller, admin);
            Assert.Equal(BookingStatus.Cancelled, (await service.GetBookingByIdAsync(booking.Id, caller, admin))!.Status);
            Assert.False(await service.MarkAsProcessedAsync(booking.Id, BookingStatus.Confirmed));
        }
    }

    [Theory]
    [InlineData(BookingStatus.Confirmed)]
    [InlineData(BookingStatus.Rejected)]
    public async Task Processing_ChangesPendingOnly(BookingStatus target)
    {
        using var db = new TestDatabase();
        using var scope = db.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IBookingService>();
        var booking = await service.CreateBookingAsync(Guid.NewGuid(), db.UserId);
        Assert.True(await service.MarkAsProcessedAsync(booking.Id, target));
        Assert.False(await service.MarkAsProcessedAsync(booking.Id, target));
        Assert.False(await service.MarkAsProcessedAsync(Guid.NewGuid(), target));
        var stored = await service.GetBookingByIdAsync(booking.Id, db.UserId, false);
        Assert.Equal(target, stored!.Status);
        Assert.NotNull(stored.ProcessedAt);
        Assert.Empty(await service.GetPendingBookingsAsync());
    }

    [Fact]
    public async Task ActiveLimit_IsPerUserAndCancellationFreesSlot()
    {
        using var db = new TestDatabase();
        using var scope = db.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IBookingService>();
        for (var i = 0; i < BookingService.MaxActiveBookings; i++)
            await service.CreateBookingAsync(Guid.NewGuid(), db.UserId);
        var first = (await service.GetBookingsAsync())[0];
        await service.MarkAsProcessedAsync(first.Id, BookingStatus.Confirmed);
        await Assert.ThrowsAsync<ActiveBookingLimitExceededException>(() => service.CreateBookingAsync(Guid.NewGuid(), db.UserId));
        await service.CreateBookingAsync(Guid.NewGuid(), db.OtherUserId);
        await service.CancelBookingAsync(first.Id, db.UserId, false);
        await service.CreateBookingAsync(Guid.NewGuid(), db.UserId);
    }

    [Fact]
    public async Task ConcurrentRequests_DoNotExceedLocalLimit()
    {
        using var db = new TestDatabase();
        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(async _ =>
        {
            using var scope = db.CreateScope();
            try
            {
                await scope.ServiceProvider.GetRequiredService<IBookingService>().CreateBookingAsync(Guid.NewGuid(), db.UserId);
                return true;
            }
            catch (ActiveBookingLimitExceededException) { return false; }
        }));
        Assert.Equal(BookingService.MaxActiveBookings, results.Count(x => x));
    }

    [Fact]
    public async Task MissingCancel_AndInvalidIds_AreRejected()
    {
        using var db = new TestDatabase();
        using var scope = db.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IBookingService>();
        await Assert.ThrowsAsync<NotFoundException>(() => service.CancelBookingAsync(Guid.NewGuid(), db.UserId, false));
        await Assert.ThrowsAsync<DomainValidationException>(() => service.CreateBookingAsync(Guid.Empty, db.UserId));
        await Assert.ThrowsAsync<DomainValidationException>(() => service.CreateBookingAsync(Guid.NewGuid(), Guid.Empty));
        Assert.Empty(await service.GetBookingsAsync());
    }
}
