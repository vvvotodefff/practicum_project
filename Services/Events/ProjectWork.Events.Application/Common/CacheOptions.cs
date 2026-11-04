namespace ProjectWork.Events.Application.Common;

/// <summary>
/// Время жизни записей кеша
/// </summary>
public sealed record CacheOptions(TimeSpan EventTtl, TimeSpan TopEventsTtl);