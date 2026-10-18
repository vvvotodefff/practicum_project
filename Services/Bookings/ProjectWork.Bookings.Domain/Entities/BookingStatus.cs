namespace ProjectWork.Bookings.Domain.Entities;

/// <summary>
/// Статус бронирования
/// </summary>
public enum BookingStatus
{
    /// <summary>
    /// Бронь создана и ожидает обработки
    /// </summary>
    Pending,

    /// <summary>
    /// Бронь подтверждена
    /// </summary>
    Confirmed,

    /// <summary>
    /// Бронь отклонена
    /// </summary>
    Rejected,

    /// <summary>
    /// Бронь отменена
    /// </summary>
    Cancelled
}
