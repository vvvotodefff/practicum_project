using ProjectWork.Events.Application.Abstractions;
using ProjectWork.Events.Application.Abstractions.Repositories;
using ProjectWork.Events.Application.DTO;
using ProjectWork.Events.Domain.Exceptions;
using ProjectWork.Events.Domain.Entities;
using ProjectWork.Events.Application.Common;

namespace ProjectWork.Events.Application.Services;

/// <summary>Прикладные операции с событиями через порт репозитория.</summary>
/// <param name="eventRepository">Доступ к событиям в текущем scope.</param>
/// <param name="processedBookings">Учёт уже обработанных подтверждений броней.</param>
/// <param name="unitOfWork">Атомарное сохранение изменений текущего scope.</param>
/// <param name="cache">Кеш событий и топа (Cache-Aside).</param>
/// <param name="cacheOptions">Время жизни записей кеша.</param>
public class EventService(IEventRepository eventRepository,
    IProcessedBookingRepository processedBookings, IUnitOfWork unitOfWork,
    ICacheService cache, CacheOptions cacheOptions) : IEventService
{
    /// <inheritdoc />
    public async Task<PaginatedResult<EventInfo>> GetEventsAsync(string? title, DateTime? from,
        DateTime? to, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var result = await eventRepository.GetPageAsync(title,
            from.HasValue ? Event.ToUtc(from.Value) : null,
            to.HasValue ? Event.ToUtc(to.Value) : null,
            page, pageSize, cancellationToken);
        return new PaginatedResult<EventInfo>
        {
            TotalCount = result.TotalCount, Page = result.Page, PageSize = result.PageSize,
            Items = result.Items.Select(EventInfo.FromEvent).ToList()
        };
    }

    /// <inheritdoc />
    public async Task<EventInfo?> GetEventByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var key = CacheKeys.Event(id);
        var cached = await cache.GetAsync<EventInfo>(key, cancellationToken);
        if (cached is not null) return cached;

        var eventItem = await eventRepository.GetByIdAsync(id, cancellationToken);
        if (eventItem is null) return null;

        var result = EventInfo.FromEvent(eventItem);
        await cache.SetAsync(key, result, cacheOptions.EventTtl, cancellationToken);
        return result;
    }

    /// <inheritdoc />
    public async Task<EventInfo> CreateEventAsync(CreateEvent request, CancellationToken cancellationToken = default)
    {
        var eventItem = Event.Create(request.Title, request.Description,
            request.StartAt, request.EndAt, request.TotalSeats ?? 0);
        await eventRepository.AddAsync(eventItem, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    
        return EventInfo.FromEvent(eventItem);
    }

    /// <inheritdoc />
    public async Task<bool> UpdateEventAsync(Guid id, UpdateEvent request,
        CancellationToken cancellationToken = default)
    {
        await EventWriteLock.Gate.WaitAsync(cancellationToken);
        try
        {
            var eventItem = await eventRepository.GetByIdForUpdateAsync(id, cancellationToken);
            if (eventItem is null) return false;
            eventItem.Update(request.Title, request.Description, request.StartAt,
                request.EndAt, request.TotalSeats ?? 0);
            await eventRepository.UpdateAsync(eventItem, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await cache.RemoveAsync(CacheKeys.Event(id), cancellationToken);
            return true;
        }
        finally { EventWriteLock.Gate.Release(); }
    }

    /// <inheritdoc />
    public async Task<bool> DeleteEventAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await EventWriteLock.Gate.WaitAsync(cancellationToken);
        try
        {
            if (!await eventRepository.DeleteAsync(id, cancellationToken)) return false;
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await cache.RemoveAsync(CacheKeys.Event(id), cancellationToken);
            return true;
        }
        finally { EventWriteLock.Gate.Release(); }
    }

    /// <inheritdoc />
    public async Task<bool> TryReserveSeatsAsync(Guid eventId, int count = 1,
        CancellationToken cancellationToken = default)
    {
        await EventWriteLock.Gate.WaitAsync(cancellationToken);
        try
        {
            var eventItem = await eventRepository.GetByIdForUpdateAsync(eventId, cancellationToken)
                ?? throw new NotFoundException($"Событие с идентификатором '{eventId}' не найдено.");
            if (!eventItem.TryReserveSeats(count)) return false;
            await eventRepository.UpdateAsync(eventItem, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await cache.RemoveAsync(CacheKeys.Event(eventId), cancellationToken);
            return true;
        }
        finally { EventWriteLock.Gate.Release(); }
    }

    /// <inheritdoc />
    public async Task ReleaseSeatsAsync(Guid eventId, int count = 1,
        CancellationToken cancellationToken = default)
    {
        await EventWriteLock.Gate.WaitAsync(cancellationToken);
        try
        {
            var eventItem = await eventRepository.GetByIdForUpdateAsync(eventId, cancellationToken);
            if (eventItem is null) return;
            eventItem.ReleaseSeats(count);
            await eventRepository.UpdateAsync(eventItem, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await cache.RemoveAsync(CacheKeys.Event(eventId), cancellationToken);
        }
        finally { EventWriteLock.Gate.Release(); }
    }

    /// <inheritdoc />
    public async Task<BookingConfirmationResult> ApplyBookingConfirmedAsync(Guid bookingId, Guid eventId,
        int seatsCount, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(seatsCount);
        await EventWriteLock.Gate.WaitAsync(cancellationToken);
        try
        {
            if (await processedBookings.ExistsAsync(bookingId, cancellationToken))
                return BookingConfirmationResult.AlreadyProcessed;

            var eventItem = await eventRepository.GetByIdForUpdateAsync(eventId, cancellationToken);
            var result = eventItem is null ? BookingConfirmationResult.EventNotFound
                : eventItem.TryReserveSeats(seatsCount) ? BookingConfirmationResult.Applied
                : BookingConfirmationResult.NoAvailableSeats;
            if (result == BookingConfirmationResult.Applied)
                await eventRepository.UpdateAsync(eventItem!, cancellationToken);

            // Отмечаем и пропущенные сообщения: повторная доставка даёт тот же итог.
            await processedBookings.AddAsync(bookingId, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            if (result == BookingConfirmationResult.Applied) 
            {
                await cache.RemoveAsync(CacheKeys.Event(eventId), cancellationToken);
            }

            return result;
        }
        finally { EventWriteLock.Gate.Release(); }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<EventInfo>> GetTopEventsAsync(CancellationToken cancellationToken = default)
    {
        var cached = await cache.GetAsync<List<EventInfo>>(CacheKeys.TopEvents, cancellationToken);
        if (cached is not null) return cached;

        var top = (await eventRepository.GetTopBySoldPercentAsync(10, cancellationToken))
            .Select(EventInfo.FromEvent).ToList();
        await cache.SetAsync(CacheKeys.TopEvents, top, cacheOptions.TopEventsTtl, cancellationToken);
        return top;
    }
}
