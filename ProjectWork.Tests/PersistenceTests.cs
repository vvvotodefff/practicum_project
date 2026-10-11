using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ProjectWork.Infrastructure.Persistence;
using ProjectWork.Application.DTO;
using ProjectWork.Application.Services;
using ProjectWork.Domain.Entities;
using ProjectWork.Infrastructure.BackgroundServices;

namespace ProjectWork.Tests;

public class PersistenceTests : IDisposable
{
    private readonly TestDatabase _database = new();
    public void Dispose() => _database.Dispose();

    private async Task<Guid> CreateEventAsync(int seats = 3)
    {
        using var scope = _database.CreateScope();
        var created = await scope.ServiceProvider.GetRequiredService<IEventService>()
            .CreateEventAsync(new CreateEvent
            {
                Title = "Persistence", TotalSeats = seats,
                StartAt = new DateTime(2026, 11, 1, 12, 0, 0),
                EndAt = new DateTime(2026, 11, 1, 13, 0, 0)
            });
        return created.Id;
    }

    [Fact]
    public async Task NewScope_SeesSavedBookingAndSeatCounter()
    {
        var id = await CreateEventAsync();
        Guid bookingId;
        using (var scope = _database.CreateScope())
        {
            bookingId = (await scope.ServiceProvider.GetRequiredService<IBookingService>()
                .CreateBookingAsync(id)).Id;
        }
        using var readScope = _database.CreateScope();
        var context = readScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var booking = await context.Bookings.Include(b => b.Event).SingleAsync(b => b.Id == bookingId);
        Assert.Equal(2, booking.Event.AvailableSeats);
        Assert.Equal(DateTimeKind.Utc, booking.Event.StartAt.Kind);
        Assert.Equal(BookingStatus.Pending, booking.Status);
    }

    [Fact]
    public async Task Reads_AreNotTracked()
    {
        var id = await CreateEventAsync();
        using var scope = _database.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IEventService>();
        var detached = await service.GetEventByIdAsync(id);
        detached!.Title = "Not saved";
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Empty(context.ChangeTracker.Entries());
        await context.SaveChangesAsync();
        Assert.Equal("Persistence", (await service.GetEventByIdAsync(id))!.Title);
    }

    [Fact]
    public async Task DeleteEvent_RemovesItsBookings()
    {
        var id = await CreateEventAsync();
        using (var scope = _database.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IBookingService>().CreateBookingAsync(id);
        using (var scope = _database.CreateScope())
            Assert.True(await scope.ServiceProvider.GetRequiredService<IEventService>().DeleteEventAsync(id));
        using var readScope = _database.CreateScope();
        var context = readScope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Empty(await context.Events.ToListAsync());
        Assert.Empty(await context.Bookings.ToListAsync());
    }

    [Fact]
    public async Task ConcurrentRejection_ReturnsSeatOnlyOnce()
    {
        var id = await CreateEventAsync(2);
        Guid bookingId;
        using (var scope = _database.CreateScope())
            bookingId = (await scope.ServiceProvider.GetRequiredService<IBookingService>().CreateBookingAsync(id)).Id;
        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => Task.Run(async () =>
        {
            using var scope = _database.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<IBookingService>()
                .MarkAsProcessedAsync(bookingId, BookingStatus.Rejected);
        })));
        Assert.Single(results, value => value);
        using var readScope = _database.CreateScope();
        var context = readScope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(2, (await context.Events.SingleAsync()).AvailableSeats);
        Assert.Equal(BookingStatus.Rejected, (await context.Bookings.SingleAsync()).Status);
    }

    [Fact]
    public async Task LongLivedScope_RefreshesSeatsAfterAnotherScopeBooks()
    {
        var id = await CreateEventAsync(3);
        using var scope = _database.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IEventService>();
        await service.TryReserveSeatsAsync(id);
        using (var other = _database.CreateScope())
            await other.ServiceProvider.GetRequiredService<IBookingService>().CreateBookingAsync(id);
        await service.UpdateEventAsync(id, new UpdateEvent
        {
            Title = "Updated", TotalSeats = 5,
            StartAt = new DateTime(2026, 11, 1, 12, 0, 0),
            EndAt = new DateTime(2026, 11, 1, 13, 0, 0)
        });
        Assert.Equal(3, (await service.GetEventByIdAsync(id))!.AvailableSeats);
    }

    [Fact]
    public async Task BackgroundService_ProcessesPersistedPendingBookingsInSeparateScopes()
    {
        var id = await CreateEventAsync(12);
        using (var scope = _database.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<IBookingService>();
            for (var i = 0; i < 12; i++) await service.CreateBookingAsync(id);
        }
        using var worker = new BookingProcessingService(
            _database.Provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<BookingProcessingService>.Instance);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await worker.StartAsync(timeout.Token);
        try
        {
            while (true)
            {
                using var scope = _database.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var bookings = await context.Bookings.AsNoTracking().ToListAsync(timeout.Token);
                if (bookings.All(b => b.Status == BookingStatus.Confirmed))
                {
                    Assert.Equal(12, bookings.Count);
                    Assert.All(bookings, b => Assert.NotNull(b.ProcessedAt));
                    break;
                }
                await Task.Delay(100, timeout.Token);
            }
        }
        finally { await worker.StopAsync(CancellationToken.None); }
    }

    [Theory]
    [InlineData(BookingStatus.Confirmed)]
    [InlineData(BookingStatus.Rejected)]
    public async Task ProcessedBooking_CannotBeProcessedAgain(BookingStatus finalStatus)
    {
        var id = await CreateEventAsync(1);
        using var scope = _database.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IBookingService>();
        var booking = await service.CreateBookingAsync(id);
        Assert.True(await service.MarkAsProcessedAsync(booking.Id, finalStatus));
        Assert.False(await service.MarkAsProcessedAsync(booking.Id, BookingStatus.Rejected));
        var expectedSeats = finalStatus == BookingStatus.Rejected ? 1 : 0;
        Assert.Equal(expectedSeats,
            (await scope.ServiceProvider.GetRequiredService<IEventService>().GetEventByIdAsync(id))!.AvailableSeats);
    }
}
