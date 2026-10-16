using ProjectWork.Domain.Entities;

namespace ProjectWork.Application.Abstractions.Repositories;

/// <summary>Доступ к бронированиям без правил перехода между статусами.</summary>
public interface IBookingRepository
{
    /// <summary>Получить брони без отслеживания изменений, при необходимости отфильтровав по статусу.</summary>
    Task<List<Booking>> GetAllAsync(BookingStatus? status = null, CancellationToken cancellationToken = default);

    /// <summary>Получить бронь для чтения без отслеживания изменений.</summary>
    Task<Booking?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Получить отслеживаемую бронь, перечитав её актуальное состояние из базы.</summary>
    Task<Booking?> GetByIdForUpdateAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Посчитать Pending и Confirmed брони пользователя в базе.</summary>
    Task<int> CountActiveByUserAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Получить только идентификаторы броней с указанным статусом.</summary>
    Task<List<Guid>> GetIdsByStatusAsync(BookingStatus status, CancellationToken cancellationToken = default);

    /// <summary>Подготовить добавление брони; сохранение выполняет IUnitOfWork.</summary>
    Task AddAsync(Booking booking, CancellationToken cancellationToken = default);

    /// <summary>Подготовить обновление брони; сохранение выполняет IUnitOfWork.</summary>
    Task UpdateAsync(Booking booking, CancellationToken cancellationToken = default);
}
