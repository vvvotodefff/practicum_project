namespace ProjectWork.Events.Application.Common;

/// <summary>
/// Все ключи кеша сервиса событий
/// </summary>
public static class CacheKeys
{
    /// <summary>
    /// Топ-10 событий по проценту проданных мест
    /// </summary>
    public const string TopEvents = "events:top10";

    /// <summary>
    /// Отдельное событие
    /// </summary>
    public static string Event(Guid id) => $"event:{id}";
}