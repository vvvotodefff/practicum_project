namespace ProjectWork.Domain.Entities;

/// <summary>
/// Бронирование места на событии
/// </summary>
public class Booking
{
    private Booking() { }

    public Event Event { get; private set; } = null!;

    public static Booking Create(Guid eventId) => new()
    {
        Id = Guid.NewGuid(),
        EventId = eventId,
        Status = BookingStatus.Pending,
        CreatedAt = DateTime.UtcNow
    };

    /// <summary>
    /// Уникальный идентификатор брони
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Идентификатор события, к которому относится бронь
    /// </summary>
    public Guid EventId { get; set; }

    /// <summary>
    /// Текущий статус брони
    /// </summary>
    public BookingStatus Status { get; set; }

    /// <summary>
    /// Дата и время создания брони
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Дата и время обработки брони. Пока бронь не обработана — null
    /// </summary>
    public DateTime? ProcessedAt { get; set; }

    /// <summary>
    /// Подтвердить бронь и отметить время обработки
    /// </summary>
    public void Confirm()
    {
        Status = BookingStatus.Confirmed;
        ProcessedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Отклонить бронь и отметить время обработки
    /// </summary>
    public void Reject()
    {
        Status = BookingStatus.Rejected;
        ProcessedAt = DateTime.UtcNow;
    }
}
