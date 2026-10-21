using ProjectWork.Events.Application.DTO;
using ProjectWork.Events.Application.Common;

namespace ProjectWork.Events.Application.Services;

/// <summary>Асинхронные операции с событиями и количеством свободных мест.</summary>
public interface IEventService
{
    /// <summary>Получить страницу событий с фильтрами по названию и датам.</summary>
    Task<PaginatedResult<EventInfo>> GetEventsAsync(string? title, DateTime? from, DateTime? to,
        int page, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>Получить событие по идентификатору или null, если его нет.</summary>
    Task<EventInfo?> GetEventByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Создать событие из данных запроса. Бросает DomainValidationException, если данные некорректны.
    /// </summary>
    Task<EventInfo> CreateEventAsync(CreateEvent request, CancellationToken cancellationToken = default);

    /// <summary>Обновить событие. Свободные места пересчитываются сервером.</summary>
    Task<bool> UpdateEventAsync(Guid id, UpdateEvent request, CancellationToken cancellationToken = default);

    /// <summary>Удалить событие и его брони. Возвращает false, если события нет.</summary>
    Task<bool> DeleteEventAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Занять места на событии. Возвращает false, если свободных мест не хватает.
    /// Бросает NotFoundException, если события нет.
    /// </summary>
    Task<bool> TryReserveSeatsAsync(Guid eventId, int count = 1, CancellationToken cancellationToken = default);

    /// <summary>Вернуть места в пул. Если события уже нет, ничего не делает.</summary>
    Task ReleaseSeatsAsync(Guid eventId, int count = 1, CancellationToken cancellationToken = default);

    /// <summary>
    /// Занять места по подтверждённой брони ровно один раз. Повторное подтверждение
    /// той же брони места не списывает. Отметка об обработке и изменение мест сохраняются вместе.
    /// </summary>
    Task<BookingConfirmationResult> ApplyBookingConfirmedAsync(Guid bookingId, Guid eventId,
        int seatsCount, CancellationToken cancellationToken = default);

    /// <summary>
    /// Получить 10 событий с наибольшим процентом проданных мест.
    /// </summary>
    Task<IReadOnlyList<EventInfo>> GetTopEventsAsync(CancellationToken cancellationToken = default);
}
