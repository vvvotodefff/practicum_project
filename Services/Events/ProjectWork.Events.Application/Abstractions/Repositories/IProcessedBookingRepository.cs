namespace ProjectWork.Events.Application.Abstractions.Repositories;

/// <summary>
/// Учёт уже обработанных подтверждений броней для идемпотентного приёма сообщений
/// </summary>
public interface IProcessedBookingRepository
{
    /// <summary>
    /// Проверить, было ли подтверждение брони уже обработано
    /// </summary>
    Task<bool> ExistsAsync(Guid bookingId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Подготовить отметку об обработке брони; сохранение выполняет IUnitOfWork
    /// </summary>
    Task AddAsync(Guid bookingId, CancellationToken cancellationToken = default);
}
