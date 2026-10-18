using ProjectWork.Bookings.Domain.Entities;
using ProjectWork.Bookings.Application.DTO;

namespace ProjectWork.Bookings.Application.Services;

/// <summary>Асинхронное создание, чтение и обработка бронирований.</summary>
public interface IBookingService
{
    /// <summary>Получить все брони.</summary>
    Task<List<BookingInfo>> GetBookingsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Создать бронь для события: присваивает новый идентификатор,
    /// статус <see cref="BookingStatus.Pending"/> и текущее время создания.
    /// Сохраняет только бронь; изменение мест выполняется сервисом событий.
    /// </summary>
    Task<BookingInfo> CreateBookingAsync(Guid eventId, Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Получить свою бронь, либо любую для Admin; чужая и отсутствующая бронь дают null.</summary>
    Task<BookingInfo?> GetBookingByIdAsync(Guid bookingId, Guid userId, bool isAdmin, CancellationToken cancellationToken = default);

    /// <summary>Отменить свою бронь, либо любую бронь для администратора.</summary>
    Task CancelBookingAsync(Guid bookingId, Guid userId, bool isAdmin,
        CancellationToken cancellationToken = default);

    /// <summary>Получить брони, ожидающие обработки (статус <see cref="BookingStatus.Pending"/>).</summary>
    Task<List<BookingInfo>> GetPendingBookingsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Подтвердить или отклонить ожидающую бронь и проставить время обработки.
    /// Возвращает false, если брони нет,
    /// она уже обработана или передан неокончательный статус.
    /// </summary>
    Task<bool> MarkAsProcessedAsync(Guid bookingId, BookingStatus status,
        CancellationToken cancellationToken = default);
}
