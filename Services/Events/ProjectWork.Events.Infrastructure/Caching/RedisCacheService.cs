using System.Text.Json;
using Microsoft.Extensions.Logging;
using ProjectWork.Events.Application.Abstractions;
using StackExchange.Redis;

namespace ProjectWork.Events.Infrastructure.Caching;

/// <summary>
/// Кеш на Redis: значения хранятся строками в JSON.
/// Ошибки Redis только логируются — при недоступном кеше данные берутся из базы
/// </summary>
public class RedisCacheService(IConnectionMultiplexer redis, ILogger<RedisCacheService> logger) : ICacheService
{
    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            var value = await redis.GetDatabase().StringGetAsync(key);
            return value.IsNullOrEmpty ? default : JsonSerializer.Deserialize<T>(value.ToString());
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Не удалось прочитать ключ {Key} из Redis, запрос пойдёт в базу", key);
            return default;
        }
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan expiry, CancellationToken cancellationToken = default)
    {
        try
        {
            await redis.GetDatabase().StringSetAsync(key, JsonSerializer.Serialize(value), expiry);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Не удалось записать ключ {Key} в Redis", key);
        }
    }

    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            await redis.GetDatabase().KeyDeleteAsync(key);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Не удалось удалить ключ {Key} из Redis", key);
        }
    }
}
