using Microsoft.Extensions.DependencyInjection;
using ProjectWork.Application.DTO;
using ProjectWork.Application.Services;
using ProjectWork.Domain.Entities;

namespace ProjectWork.Tests;

public class ServiceDtoTests
{
    [Fact]
    public async Task ServiceResponses_AreIndependentSnapshotsOfPersistedData()
    {
        using var database = new TestDatabase();
        using var scope = database.CreateScope();
        var events = scope.ServiceProvider.GetRequiredService<IEventService>();
        var bookings = scope.ServiceProvider.GetRequiredService<IBookingService>();
        var created = await events.CreateEventAsync(new CreateEvent
        {
            Title = "Original",
            StartAt = new DateTime(2026, 11, 1, 10, 0, 0, DateTimeKind.Utc),
            EndAt = new DateTime(2026, 11, 1, 11, 0, 0, DateTimeKind.Utc),
            TotalSeats = 3
        });

        created.Title = "Client changed DTO";
        var booking = await bookings.CreateBookingAsync(created.Id);
        Assert.True(await bookings.MarkAsProcessedAsync(booking.Id, BookingStatus.Confirmed));

        var found = await events.GetEventByIdAsync(created.Id);
        Assert.NotNull(found);
        Assert.Equal("Original", found.Title);
        Assert.Equal(2, found.AvailableSeats);
        Assert.Equal(3, created.AvailableSeats);
        Assert.Equal(BookingStatus.Pending, booking.Status);
        Assert.Equal(BookingStatus.Confirmed, (await bookings.GetBookingByIdAsync(booking.Id))!.Status);

        var page = await events.GetEventsAsync(null, null, null, 1, 10);
        Assert.Equal(found.Id, Assert.Single(page.Items).Id);
        Assert.Equal("Original", page.Items[0].Title);
        Assert.Equal(BookingStatus.Confirmed, Assert.Single(await bookings.GetBookingsAsync()).Status);
        Assert.Empty(await bookings.GetPendingBookingsAsync());
    }
}
