using ProjectWork.Events.Application.Abstractions;
using ProjectWork.Events.Application.Abstractions.Repositories;
using ProjectWork.Events.Application.DTO;
using ProjectWork.Events.Domain.Exceptions;
using ProjectWork.Events.Domain.Entities;
using ProjectWork.Events.Application.Common;

namespace ProjectWork.Events.Application.Services;

/// <summary>Прикладные операции с событиями через порт репозитория.</summary>
/// <param name="eventRepository">Доступ к событиям в текущем scope.</param>
/// <param name="unitOfWork">Атомарное сохранение изменений текущего scope.</param>
public class EventService(IEventRepository eventRepository, IUnitOfWork unitOfWork) : IEventService
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
        var eventItem = await eventRepository.GetByIdAsync(id, cancellationToken);
        return eventItem is null ? null : EventInfo.FromEvent(eventItem);
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
        }
        finally { EventWriteLock.Gate.Release(); }
    }
}
