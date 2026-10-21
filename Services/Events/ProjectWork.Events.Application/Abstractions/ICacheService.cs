namespace ProjectWork.Events.Application.Abstractions;

/// <summary>
/// Кеш для часто читаемых данных. Недоступность кеша не должна ломать запрос:
/// при ошибке чтение ведёт себя как промах, а запись и удаление пропускаются
/// </summary>
public interface ICacheService
{
    /// <summary>
    /// Получить значение по ключу; null — если ключа нет или кеш недоступен
    /// </summary>
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Записать значение с временем жизни
    /// </summary>
    Task SetAsync<T>(string key, T value, TimeSpan expiry, CancellationToken cancellationToken = default);

    /// <summary>
    /// Удалить значение по ключу
    /// </summary>
    Task RemoveAsync(string key, CancellationToken cancellationToken = default);
}
