using Microsoft.EntityFrameworkCore;
using ProjectWork.Events.Application.Abstractions.Repositories;
using ProjectWork.Events.Domain.Entities;
using ProjectWork.Events.Application.Common;
using ProjectWork.Events.Infrastructure.Persistence;

namespace ProjectWork.Events.Infrastructure.Repositories;

/// <summary>Запросы и подготовка изменений событий через EF Core.</summary>
public sealed class EventRepository(EventsDbContext context) : IEventRepository
{
    /// <inheritdoc />
    public async Task<PaginatedResult<Event>> GetPageAsync(string? title, DateTime? from,
        DateTime? to, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(page);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageSize);

        var query = context.Events.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(title))
        {
            var search = title.ToLower();
            query = query.Where(e => e.Title.ToLower().Contains(search));
        }
        if (from.HasValue)
            query = query.Where(e => e.StartAt >= from.Value);
        if (to.HasValue)
            query = query.Where(e => e.EndAt <= to.Value);

        var totalCount = await query.CountAsync(cancellationToken);
        var offset = (long)(page - 1) * pageSize;
        var items = offset >= totalCount
            ? new List<Event>()
            : await query.OrderBy(e => e.StartAt).ThenBy(e => e.Id)
                .Skip((int)offset).Take(pageSize).ToListAsync(cancellationToken);
        return new PaginatedResult<Event>
        {
            TotalCount = totalCount, Page = page, PageSize = pageSize, Items = items
        };
    }

    /// <inheritdoc />
    public Task<Event?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        context.Events.AsNoTracking().SingleOrDefaultAsync(e => e.Id == id, cancellationToken);

    /// <inheritdoc />
    public async Task<Event?> GetByIdForUpdateAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var eventItem = await context.Events.SingleOrDefaultAsync(e => e.Id == id, cancellationToken);
        if (eventItem is null) return null;

        // В одном scope могла остаться сущность, изменённая позже другим запросом.
        await context.Entry(eventItem).ReloadAsync(cancellationToken);
        return context.Entry(eventItem).State == EntityState.Detached ? null : eventItem;
    }

    /// <inheritdoc />
    public Task AddAsync(Event eventItem, CancellationToken cancellationToken = default)
    {
        context.Events.Add(eventItem);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task UpdateAsync(Event eventItem, CancellationToken cancellationToken = default)
    {
        // Обновляем только событие, а не граф его навигационных свойств.
        context.Entry(eventItem).State = EntityState.Modified;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var eventItem = await GetByIdForUpdateAsync(id, cancellationToken);
        if (eventItem is null) return false;

        context.Events.Remove(eventItem);
        return true;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Event>> GetTopBySoldPercentAsync(int count,
        CancellationToken cancellationToken = default) =>
        await context.Events.AsNoTracking()
            // без (double) деление целочисленное и почти всё даст 0.
            .OrderByDescending(e => (double)(e.TotalSeats - e.AvailableSeats) / e.TotalSeats)
            .ThenBy(e => e.Id)
            .Take(count)
            .ToListAsync(cancellationToken);
}
