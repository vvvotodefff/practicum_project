using Microsoft.Extensions.DependencyInjection;
using ProjectWork.Application.DTO;
using ProjectWork.Domain.Exceptions;
using ProjectWork.Domain.Entities;
using ProjectWork.Application.Services;

namespace ProjectWork.Tests;

public class BookingServiceTests : IDisposable
{
    private readonly TestDatabase _database = new();
    private readonly IServiceScope _scope;
    private readonly IEventService _eventService;
    private readonly IBookingService _service;

    public BookingServiceTests()
    {
        _scope = _database.CreateScope();
        _eventService = _scope.ServiceProvider.GetRequiredService<IEventService>();
        _service = _scope.ServiceProvider.GetRequiredService<IBookingService>();
    }

    public void Dispose()
    {
        _scope.Dispose();
        _database.Dispose();
    }

    /// <summary>
    /// Создаёт событие заданной ёмкости для теста
    /// </summary>
    private async Task<EventInfo> CreateTestEvent(int totalSeats = 100)
    {
        return await _eventService.CreateEventAsync(new CreateEvent
        {
            Title = "Концерт",
            StartAt = new DateTime(2026, 9, 1, 19, 0, 0),
            EndAt = new DateTime(2026, 9, 1, 22, 0, 0),
            TotalSeats = totalSeats
        });
    }

    // ----- Успешные сценарии -----

    [Fact]
    public async Task CreateBookingAsync_ExistingEvent_ReturnsPendingBooking()
    {
        var eventItem = await CreateTestEvent();
        var before = DateTime.UtcNow;

        var booking = await _service.CreateBookingAsync(eventItem.Id, _database.UserId);

        Assert.NotEqual(Guid.Empty, booking.Id);
        Assert.Equal(eventItem.Id, booking.EventId);
        Assert.Equal(BookingStatus.Pending, booking.Status);
        Assert.InRange(booking.CreatedAt, before, DateTime.UtcNow);
        Assert.Null(booking.ProcessedAt);
    }

    [Fact]
    public async Task CreateBookingAsync_SeveralBookingsForSameEvent_GetUniqueIds()
    {
        var eventItem = await CreateTestEvent();

        var first = await _service.CreateBookingAsync(eventItem.Id, _database.UserId);
        var second = await _service.CreateBookingAsync(eventItem.Id, _database.UserId);
        var third = await _service.CreateBookingAsync(eventItem.Id, _database.UserId);

        var ids = new[] { first.Id, second.Id, third.Id };
        Assert.Equal(3, ids.Distinct().Count());
        Assert.Equal(3, (await _service.GetBookingsAsync()).Count);
    }

    [Fact]
    public async Task CreateBookingAsync_StoresBookingInStorage()
    {
        var eventItem = await CreateTestEvent();

        var booking = await _service.CreateBookingAsync(eventItem.Id, _database.UserId);

        var stored = Assert.Single((await _service.GetBookingsAsync()));
        Assert.Equal(booking.Id, stored.Id);
    }

    [Fact]
    public async Task GetBookingByIdAsync_ExistingId_ReturnsBooking()
    {
        var eventItem = await CreateTestEvent();
        var created = await _service.CreateBookingAsync(eventItem.Id, _database.UserId);

        var found = await _service.GetBookingByIdAsync(created.Id);

        Assert.NotNull(found);
        Assert.Equal(created.Id, found.Id);
        Assert.Equal(eventItem.Id, found.EventId);
        Assert.Equal(BookingStatus.Pending, found.Status);
    }

    [Theory]
    [InlineData(BookingStatus.Confirmed)]
    [InlineData(BookingStatus.Rejected)]
    public async Task GetBookingByIdAsync_AfterProcessing_ReflectsStatusChange(BookingStatus status)
    {
        var eventItem = await CreateTestEvent();
        var created = await _service.CreateBookingAsync(eventItem.Id, _database.UserId);
        var before = DateTime.UtcNow;

        var processed = await _service.MarkAsProcessedAsync(created.Id, status);

        Assert.True(processed);
        var found = await _service.GetBookingByIdAsync(created.Id);
        Assert.NotNull(found);
        Assert.Equal(status, found.Status);
        Assert.NotNull(found.ProcessedAt);
        Assert.InRange(found.ProcessedAt.Value, before, DateTime.UtcNow);
    }

    [Fact]
    public async Task GetPendingBookings_ReturnsOnlyUnprocessedBookings()
    {
        var eventItem = await CreateTestEvent();
        var first = await _service.CreateBookingAsync(eventItem.Id, _database.UserId);
        await _service.CreateBookingAsync(eventItem.Id, _database.UserId);

        await _service.MarkAsProcessedAsync(first.Id, BookingStatus.Confirmed);

        var pending = await _service.GetPendingBookingsAsync();

        Assert.Single(pending);
        Assert.DoesNotContain(pending, b => b.Id == first.Id);
    }

    // ----- Смена статуса брони -----

    [Fact]
    public async Task Confirm_SetsConfirmedStatusAndProcessedAt()
    {
        var eventItem = await CreateTestEvent();
        var booking = await _service.CreateBookingAsync(eventItem.Id, _database.UserId);
        var before = DateTime.UtcNow;

        Assert.True(await _service.MarkAsProcessedAsync(booking.Id, BookingStatus.Confirmed));
        booking = (await _service.GetBookingByIdAsync(booking.Id))!;

        Assert.Equal(BookingStatus.Confirmed, booking.Status);
        Assert.NotNull(booking.ProcessedAt);
        Assert.InRange(booking.ProcessedAt.Value, before, DateTime.UtcNow);
    }

    [Fact]
    public async Task Reject_SetsRejectedStatusAndProcessedAt()
    {
        var eventItem = await CreateTestEvent();
        var booking = await _service.CreateBookingAsync(eventItem.Id, _database.UserId);

        Assert.True(await _service.MarkAsProcessedAsync(booking.Id, BookingStatus.Rejected));
        booking = (await _service.GetBookingByIdAsync(booking.Id))!;

        Assert.Equal(BookingStatus.Rejected, booking.Status);
        Assert.NotNull(booking.ProcessedAt);
    }

    [Fact]
    public async Task Reject_AndReleaseSeats_RestoresAvailableSeats()
    {
        var eventItem = await CreateTestEvent(3);
        var booking = await _service.CreateBookingAsync(eventItem.Id, _database.UserId);
        Assert.Equal(2, (await _eventService.GetEventByIdAsync(eventItem.Id))!.AvailableSeats);

        await _service.MarkAsProcessedAsync(booking.Id, BookingStatus.Rejected);
        booking = (await _service.GetBookingByIdAsync(booking.Id))!;

        Assert.Equal(BookingStatus.Rejected, booking.Status);
        Assert.Equal(3, (await _eventService.GetEventByIdAsync(eventItem.Id))!.AvailableSeats);
    }

    [Fact]
    public async Task Reject_AndReleaseSeats_AllowsBookingTheFreedSeat()
    {
        // Единственное место занято
        var eventItem = await CreateTestEvent(1);
        var booking = await _service.CreateBookingAsync(eventItem.Id, _database.UserId);

        await Assert.ThrowsAsync<NoAvailableSeatsException>(
            () => _service.CreateBookingAsync(eventItem.Id, _database.UserId));

        // Отклоняем бронь и возвращаем место в пул
        await _service.MarkAsProcessedAsync(booking.Id, BookingStatus.Rejected);

        // Теперь место снова можно занять
        var newBooking = await _service.CreateBookingAsync(eventItem.Id, _database.UserId);

        Assert.Equal(BookingStatus.Pending, newBooking.Status);
        Assert.NotEqual(booking.Id, newBooking.Id);
        Assert.Equal(0, (await _eventService.GetEventByIdAsync(eventItem.Id))!.AvailableSeats);
    }

    [Fact]
    public async Task MarkAsProcessed_PendingStatus_ReturnsFalse()
    {
        var eventItem = await CreateTestEvent();
        var booking = await _service.CreateBookingAsync(eventItem.Id, _database.UserId);

        var result = await _service.MarkAsProcessedAsync(booking.Id, BookingStatus.Pending);

        Assert.False(result);
        Assert.Null(booking.ProcessedAt);
    }

    // ----- Места на событии -----

    [Fact]
    public async Task CreateBookingAsync_ReservesSeatOnEvent()
    {
        var eventItem = await CreateTestEvent(10);

        await _service.CreateBookingAsync(eventItem.Id, _database.UserId);
        await _service.CreateBookingAsync(eventItem.Id, _database.UserId);

        var stored = await _eventService.GetEventByIdAsync(eventItem.Id);
        Assert.NotNull(stored);
        Assert.Equal(8, stored.AvailableSeats);
        Assert.Equal(10, stored.TotalSeats);
    }

    [Fact]
    public async Task CreateBookingAsync_DecreasesAvailableSeatsByOne()
    {
        var eventItem = await CreateTestEvent(5);

        await _service.CreateBookingAsync(eventItem.Id, _database.UserId);

        var stored = await _eventService.GetEventByIdAsync(eventItem.Id);
        Assert.NotNull(stored);
        Assert.Equal(4, stored.AvailableSeats);
    }

    [Fact]
    public async Task CreateBookingAsync_BookingsUpToLimit_AllSucceedWithUniqueIds()
    {
        const int totalSeats = 5;
        var eventItem = await CreateTestEvent(totalSeats);

        var bookings = new List<BookingInfo>();
        for (var i = 0; i < totalSeats; i++)
        {
            bookings.Add(await _service.CreateBookingAsync(eventItem.Id, _database.UserId));
        }

        Assert.Equal(totalSeats, bookings.Count);
        Assert.Equal(totalSeats, bookings.Select(b => b.Id).Distinct().Count());
        Assert.All(bookings, b => Assert.Equal(BookingStatus.Pending, b.Status));
        Assert.Equal(0, (await _eventService.GetEventByIdAsync(eventItem.Id))!.AvailableSeats);
    }

    [Fact]
    public async Task CreateBookingAsync_NoSeatsLeft_ThrowsNoAvailableSeats()
    {
        var eventItem = await CreateTestEvent(1);

        var first = await _service.CreateBookingAsync(eventItem.Id, _database.UserId);

        var exception = await Assert.ThrowsAsync<NoAvailableSeatsException>(
            () => _service.CreateBookingAsync(eventItem.Id, _database.UserId));

        Assert.Equal("No available seats for this event", exception.Message);
        Assert.Equal(BookingStatus.Pending, first.Status);

        var stored = await _eventService.GetEventByIdAsync(eventItem.Id);
        Assert.NotNull(stored);
        Assert.Equal(0, stored.AvailableSeats);
    }

    [Fact]
    public async Task CreateBookingAsync_NoSeatsLeft_DoesNotCreateBooking()
    {
        var eventItem = await CreateTestEvent(1);

        await _service.CreateBookingAsync(eventItem.Id, _database.UserId);
        await Assert.ThrowsAsync<NoAvailableSeatsException>(
            () => _service.CreateBookingAsync(eventItem.Id, _database.UserId));

        // В хранилище осталась только успешная бронь
        Assert.Single((await _service.GetBookingsAsync()));
    }

    // ----- Конкурентность -----

    [Fact]
    public async Task ConcurrentBookings_DoNotOverbookEvent()
    {
        const int totalSeats = 5;
        const int requests = 20;
        var eventItem = await CreateTestEvent(totalSeats);

        // Все запросы стартуют одновременно на разных потоках пула
        var tasks = Enumerable.Range(0, requests)
            .Select(_ => Task.Run(async () =>
            {
                using var scope = _database.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<IBookingService>();
                try
                {
                    await service.CreateBookingAsync(eventItem.Id, _database.UserId);
                    return true;
                }
                catch (NoAvailableSeatsException)
                {
                    return false;
                }
            }))
            .ToArray();

        var results = await Task.WhenAll(tasks);

        Assert.Equal(totalSeats, results.Count(success => success));
        Assert.Equal(requests - totalSeats, results.Count(success => !success));
        Assert.Equal(0, (await _eventService.GetEventByIdAsync(eventItem.Id))!.AvailableSeats);
        Assert.Equal(totalSeats, (await _service.GetBookingsAsync()).Count);
    }

    [Fact]
    public async Task ConcurrentBookings_ProduceUniqueIds()
    {
        const int totalSeats = 10;
        var eventItem = await CreateTestEvent(totalSeats);

        var tasks = Enumerable.Range(0, totalSeats)
            .Select(_ => Task.Run(async () =>
            {
                using var scope = _database.CreateScope();
                return await scope.ServiceProvider.GetRequiredService<IBookingService>()
                    .CreateBookingAsync(eventItem.Id, _database.UserId);
            }))
            .ToArray();

        var bookings = await Task.WhenAll(tasks);

        Assert.Equal(totalSeats, bookings.Length);
        Assert.Equal(totalSeats, bookings.Select(b => b.Id).Distinct().Count());
        Assert.Equal(0, (await _eventService.GetEventByIdAsync(eventItem.Id))!.AvailableSeats);
    }

    // ----- Неуспешные сценарии -----

    [Fact]
    public async Task CreateBookingAsync_UnknownEvent_ThrowsNotFound()
    {
        var unknownEventId = Guid.NewGuid();

        var exception = await Assert.ThrowsAsync<NotFoundException>(
            () => _service.CreateBookingAsync(unknownEventId, _database.UserId));

        Assert.Contains(unknownEventId.ToString(), exception.Message);
        Assert.Empty((await _service.GetBookingsAsync()));
    }

    [Fact]
    public async Task CreateBookingAsync_DeletedEvent_ThrowsNotFound()
    {
        var eventItem = await CreateTestEvent();
        await _eventService.DeleteEventAsync(eventItem.Id);

        await Assert.ThrowsAsync<NotFoundException>(
            () => _service.CreateBookingAsync(eventItem.Id, _database.UserId));

        Assert.Empty((await _service.GetBookingsAsync()));
    }

    [Fact]
    public async Task GetBookingByIdAsync_UnknownId_ReturnsNull()
    {
        var eventItem = await CreateTestEvent();
        await _service.CreateBookingAsync(eventItem.Id, _database.UserId);

        var found = await _service.GetBookingByIdAsync(Guid.NewGuid());

        Assert.Null(found);
    }

    [Fact]
    public async Task GetBookings_EmptyStorage_ReturnsEmptyList()
    {
        Assert.Empty((await _service.GetBookingsAsync()));
    }

    [Fact]
    public async Task MarkAsProcessed_UnknownId_ReturnsFalse()
    {
        var processed = await _service.MarkAsProcessedAsync(Guid.NewGuid(), BookingStatus.Confirmed);

        Assert.False(processed);
    }
}
