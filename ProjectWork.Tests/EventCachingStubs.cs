using ProjectWork.Events.Application.Abstractions;
using ProjectWork.Events.Application.Abstractions.Repositories;
using ProjectWork.Events.Application.Common;
using ProjectWork.Events.Domain.Entities;

namespace ProjectWork.Tests;

// Заглушки для unit-тестов кеширования EventService: без базы и без Redis.
// Общий журнал Calls позволяет проверить порядок «сначала база, потом кеш».

internal sealed class RecordingCacheService(List<string> calls) : ICacheService
{
    public Dictionary<string, object?> Store { get; } = new();
    public Dictionary<string, TimeSpan> Expiry { get; } = new();

    public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        calls.Add($"cache.get {key}");
        return Task.FromResult(Store.TryGetValue(key, out var value) ? (T?)value : default);
    }

    public Task SetAsync<T>(string key, T value, TimeSpan expiry, CancellationToken cancellationToken = default)
    {
        calls.Add($"cache.set {key}");
        Store[key] = value;
        Expiry[key] = expiry;
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        calls.Add($"cache.remove {key}");
        Store.Remove(key);
        return Task.CompletedTask;
    }
}

internal sealed class StubEventRepository(List<string> calls) : IEventRepository
{
    public Dictionary<Guid, Event> Events { get; } = new();
    public int GetByIdCalls { get; private set; }
    public int GetTopCalls { get; private set; }

    public Task<PaginatedResult<Event>> GetPageAsync(string? title, DateTime? from, DateTime? to,
        int page, int pageSize, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<Event?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        GetByIdCalls++;
        calls.Add($"repo.get {id}");
        return Task.FromResult(Events.GetValueOrDefault(id));
    }

    public Task<Event?> GetByIdForUpdateAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(Events.GetValueOrDefault(id));

    public Task AddAsync(Event eventItem, CancellationToken cancellationToken = default)
    {
        Events[eventItem.Id] = eventItem;
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Event eventItem, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(Events.Remove(id));

    public Task<IReadOnlyList<Event>> GetTopBySoldPercentAsync(int count, CancellationToken cancellationToken = default)
    {
        GetTopCalls++;
        calls.Add("repo.top");
        IReadOnlyList<Event> top = Events.Values
            .OrderByDescending(e => (double)(e.TotalSeats - e.AvailableSeats) / e.TotalSeats)
            .ThenBy(e => e.Id)
            .Take(count)
            .ToList();
        return Task.FromResult(top);
    }
}

internal sealed class StubProcessedBookingRepository : IProcessedBookingRepository
{
    public HashSet<Guid> Processed { get; } = new();

    public Task<bool> ExistsAsync(Guid bookingId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Processed.Contains(bookingId));

    public Task AddAsync(Guid bookingId, CancellationToken cancellationToken = default)
    {
        Processed.Add(bookingId);
        return Task.CompletedTask;
    }
}

internal sealed class RecordingUnitOfWork(List<string> calls) : IUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        calls.Add("db.save");
        return Task.FromResult(1);
    }
}
