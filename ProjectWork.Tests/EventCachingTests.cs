using ProjectWork.Events.Application.Common;
using ProjectWork.Events.Application.DTO;
using ProjectWork.Events.Application.Services;
using ProjectWork.Events.Domain.Entities;

namespace ProjectWork.Tests;

// Unit-тесты Cache-Aside в EventService: кеш и репозиторий подменены заглушками.
public class EventCachingTests
{
    private static readonly CacheOptions Options = new(TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(1));

    private readonly List<string> _calls = new();
    private readonly RecordingCacheService _cache;
    private readonly StubEventRepository _repository;
    private readonly StubProcessedBookingRepository _processedBookings = new();
    private readonly EventService _service;

    public EventCachingTests()
    {
        _cache = new RecordingCacheService(_calls);
        _repository = new StubEventRepository(_calls);
        _service = new EventService(_repository, _processedBookings,
            new RecordingUnitOfWork(_calls), _cache, Options);
    }

    private Event AddEvent(string title = "Концерт", int totalSeats = 100, int soldSeats = 0)
    {
        var eventItem = Event.Create(title, null,
            new DateTime(2026, 9, 1, 19, 0, 0), new DateTime(2026, 9, 1, 22, 0, 0), totalSeats);
        if (soldSeats > 0) eventItem.TryReserveSeats(soldSeats);
        _repository.Events[eventItem.Id] = eventItem;
        return eventItem;
    }

    private static UpdateEvent NewUpdate(string title) => new()
    {
        Title = title,
        StartAt = new DateTime(2026, 9, 2, 19, 0, 0),
        EndAt = new DateTime(2026, 9, 2, 22, 0, 0),
        TotalSeats = 100
    };

    // ----- Попадание в кеш -----

    [Fact]
    public async Task GetEventByIdAsync_CacheHit_ReturnsCachedAndDoesNotCallRepository()
    {
        var id = Guid.NewGuid();
        var cached = new EventInfo { Id = id, Title = "Из кеша", TotalSeats = 10, AvailableSeats = 5 };
        _cache.Store[CacheKeys.Event(id)] = cached;

        var result = await _service.GetEventByIdAsync(id);

        Assert.Same(cached, result);
        Assert.Equal(0, _repository.GetByIdCalls);
    }

    [Fact]
    public async Task GetTopEventsAsync_CacheHit_ReturnsCachedAndDoesNotCallRepository()
    {
        var cached = new List<EventInfo> { new() { Id = Guid.NewGuid(), Title = "Из кеша" } };
        _cache.Store[CacheKeys.TopEvents] = cached;

        var result = await _service.GetTopEventsAsync();

        Assert.Same(cached, result);
        Assert.Equal(0, _repository.GetTopCalls);
    }

    // ----- Промах -----

    [Fact]
    public async Task GetEventByIdAsync_CacheMiss_LoadsFromRepositoryAndStoresWithEventTtl()
    {
        var eventItem = AddEvent("Встреча");
        var key = CacheKeys.Event(eventItem.Id);

        var result = await _service.GetEventByIdAsync(eventItem.Id);

        Assert.NotNull(result);
        Assert.Equal("Встреча", result.Title);
        Assert.Equal(1, _repository.GetByIdCalls);
        var stored = Assert.IsType<EventInfo>(_cache.Store[key]);
        Assert.Equal(eventItem.Id, stored.Id);
        Assert.Equal(Options.EventTtl, _cache.Expiry[key]);
    }

    [Fact]
    public async Task GetEventByIdAsync_SecondCall_IsServedFromCache()
    {
        var eventItem = AddEvent();

        await _service.GetEventByIdAsync(eventItem.Id);
        await _service.GetEventByIdAsync(eventItem.Id);

        Assert.Equal(1, _repository.GetByIdCalls);
    }

    [Fact]
    public async Task GetEventByIdAsync_EventNotFound_DoesNotCacheNull()
    {
        var id = Guid.NewGuid();

        var result = await _service.GetEventByIdAsync(id);

        Assert.Null(result);
        Assert.False(_cache.Store.ContainsKey(CacheKeys.Event(id)));
    }

    [Fact]
    public async Task GetTopEventsAsync_CacheMiss_LoadsFromRepositoryAndStoresWithTopTtl()
    {
        var half = AddEvent("Половина продана", totalSeats: 100, soldSeats: 50);
        var almostFull = AddEvent("Почти аншлаг", totalSeats: 10, soldSeats: 9);
        var empty = AddEvent("Пусто", totalSeats: 1000);

        var result = await _service.GetTopEventsAsync();

        Assert.Equal(1, _repository.GetTopCalls);
        Assert.Equal(new[] { almostFull.Id, half.Id, empty.Id }, result.Select(e => e.Id));
        Assert.Same(result, _cache.Store[CacheKeys.TopEvents]);
        Assert.Equal(Options.TopEventsTtl, _cache.Expiry[CacheKeys.TopEvents]);
    }

    // ----- Инвалидация при записи -----

    [Fact]
    public async Task UpdateEventAsync_RemovesEventKeyAfterSavingToDatabase()
    {
        var eventItem = AddEvent();
        var key = CacheKeys.Event(eventItem.Id);
        _cache.Store[key] = EventInfo.FromEvent(eventItem);

        Assert.True(await _service.UpdateEventAsync(eventItem.Id, NewUpdate("Новое название")));

        Assert.False(_cache.Store.ContainsKey(key));
        Assert.True(_calls.IndexOf("db.save") < _calls.IndexOf($"cache.remove {key}"));
    }

    [Fact]
    public async Task UpdateEventAsync_NextReadReturnsFreshDataFromRepository()
    {
        var eventItem = AddEvent("Старое название");
        await _service.GetEventByIdAsync(eventItem.Id);

        await _service.UpdateEventAsync(eventItem.Id, NewUpdate("Новое название"));
        var result = await _service.GetEventByIdAsync(eventItem.Id);

        Assert.Equal("Новое название", result!.Title);
        Assert.Equal(2, _repository.GetByIdCalls);
    }

    [Fact]
    public async Task UpdateEventAsync_EventNotFound_DoesNotTouchCache()
    {
        Assert.False(await _service.UpdateEventAsync(Guid.NewGuid(), NewUpdate("Нет такого")));

        Assert.DoesNotContain(_calls, c => c.StartsWith("cache.remove"));
    }

    [Fact]
    public async Task UpdateEventAsync_DoesNotInvalidateTopEvents()
    {
        var eventItem = AddEvent();
        var top = new List<EventInfo> { EventInfo.FromEvent(eventItem) };
        _cache.Store[CacheKeys.TopEvents] = top;

        await _service.UpdateEventAsync(eventItem.Id, NewUpdate("Новое название"));

        // Топ — рейтинговый агрегат, он живёт по TTL без явной инвалидации.
        Assert.Same(top, _cache.Store[CacheKeys.TopEvents]);
    }

    [Fact]
    public async Task DeleteEventAsync_RemovesEventKeyAfterSavingToDatabase()
    {
        var eventItem = AddEvent();
        var key = CacheKeys.Event(eventItem.Id);
        _cache.Store[key] = EventInfo.FromEvent(eventItem);

        Assert.True(await _service.DeleteEventAsync(eventItem.Id));

        Assert.False(_cache.Store.ContainsKey(key));
        Assert.True(_calls.IndexOf("db.save") < _calls.IndexOf($"cache.remove {key}"));
    }

    [Fact]
    public async Task ApplyBookingConfirmedAsync_Applied_RemovesEventKeyAfterSavingToDatabase()
    {
        var eventItem = AddEvent();
        var key = CacheKeys.Event(eventItem.Id);
        _cache.Store[key] = EventInfo.FromEvent(eventItem);

        var result = await _service.ApplyBookingConfirmedAsync(Guid.NewGuid(), eventItem.Id, 2);

        Assert.Equal(BookingConfirmationResult.Applied, result);
        Assert.False(_cache.Store.ContainsKey(key));
        Assert.True(_calls.IndexOf("db.save") < _calls.IndexOf($"cache.remove {key}"));
    }

    [Fact]
    public async Task ApplyBookingConfirmedAsync_AlreadyProcessed_KeepsCachedEvent()
    {
        var eventItem = AddEvent();
        var key = CacheKeys.Event(eventItem.Id);
        _cache.Store[key] = EventInfo.FromEvent(eventItem);
        var bookingId = Guid.NewGuid();
        _processedBookings.Processed.Add(bookingId);

        var result = await _service.ApplyBookingConfirmedAsync(bookingId, eventItem.Id, 2);

        Assert.Equal(BookingConfirmationResult.AlreadyProcessed, result);
        Assert.True(_cache.Store.ContainsKey(key));
    }
}
