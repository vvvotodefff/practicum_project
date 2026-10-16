using ProjectWork.Domain.Exceptions;

namespace ProjectWork.Domain.Entities;

/// <summary>
/// Бронирование места на событии
/// </summary>
public class Booking
{
    private Booking() { }

    public Event Event { get; private set; } = null!;


    public static Booking Create(Guid eventId, Guid userId)
    {
        if (eventId == Guid.Empty)
        {
            throw new DomainValidationException("Идентификатор события обязателен.");
        }

        if (userId == Guid.Empty)
        {
            throw new DomainValidationException("Идентификатор пользователя обязателен.");
        }

        return new Booking
        {
            Id = Guid.NewGuid(),
            EventId = eventId,
            UserId = userId,
            Status = BookingStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Отменить активную бронь.
    /// Возвращает true, если статус изменился.
    /// </summary>
    public bool Cancel()
    {
        if (Status is not (
            BookingStatus.Pending or BookingStatus.Confirmed))
        {
            return false;
        }

        Status = BookingStatus.Cancelled;
        return true;
    }



    /// <summary>
    /// Уникальный идентификатор брони
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Идентификатор события, к которому относится бронь
    /// </summary>
    public Guid EventId { get; set; }

    /// <summary>
    /// Идентификатор пользователя, создавшего бронь
    /// </summary>
    public Guid UserId { get; private set; }

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
    /// Подтвердить ожидающую бронь
    /// </summary>
    public void Confirm()
    {
        if (Status != BookingStatus.Pending)
        {
            throw new DomainValidationException(
                "Подтвердить можно только ожидающую бронь.");
        }

        Status = BookingStatus.Confirmed;
        ProcessedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Отклонить ожидающую бронь
    /// </summary>
    public void Reject()
    {
        if (Status != BookingStatus.Pending)
        {
            throw new DomainValidationException(
                "Отклонить можно только ожидающую бронь.");
        }

        Status = BookingStatus.Rejected;
        ProcessedAt = DateTime.UtcNow;
    }
}
