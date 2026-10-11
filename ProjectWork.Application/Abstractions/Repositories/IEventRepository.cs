using ProjectWork.Domain.Entities;
using ProjectWork.Application.Common;

namespace ProjectWork.Application.Abstractions.Repositories;

/// <summary>Доступ к событиям без бизнес-правил бронирования.</summary>
public interface IEventRepository
{
    /// <summary>Получить страницу событий без отслеживания изменений; даты фильтров должны быть в UTC.</summary>
    Task<PaginatedResult<Event>> GetPageAsync(string? title, DateTime? from, DateTime? to,
        int page, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>Получить событие для чтения без отслеживания изменений.</summary>
    Task<Event?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Получить отслеживаемое событие, перечитав его актуальное состояние из базы.</summary>
    Task<Event?> GetByIdForUpdateAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Подготовить добавление события; сохранение выполняет IUnitOfWork.</summary>
    Task AddAsync(Event eventItem, CancellationToken cancellationToken = default);

    /// <summary>Подготовить обновление события; сохранение выполняет IUnitOfWork.</summary>
    Task UpdateAsync(Event eventItem, CancellationToken cancellationToken = default);

    /// <summary>Подготовить удаление события и броней; вернуть false, если события нет. Сохранение выполняет IUnitOfWork.</summary>
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
