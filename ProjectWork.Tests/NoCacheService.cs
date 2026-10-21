using ProjectWork.Events.Application.Abstractions;

namespace ProjectWork.Tests;

// Кеш, который всегда даёт промах: сервис событий в этих тестах работает только с базой.
internal sealed class NoCacheService : ICacheService
{
    public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) =>
        Task.FromResult<T?>(default);

    public Task SetAsync<T>(string key, T value, TimeSpan expiry, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task RemoveAsync(string key, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
