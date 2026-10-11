using ProjectWork.Domain.Entities;

namespace ProjectWork.Application.DTO;

/// <summary>Состояние бронирования без навигационных свойств доменной сущности.</summary>
public class BookingInfo
{
    /// <summary>Идентификатор брони.</summary>
    public Guid Id { get; set; }

    /// <summary>Идентификатор события.</summary>
    public Guid EventId { get; set; }

    /// <summary>Текущий статус брони.</summary>
    public BookingStatus Status { get; set; }

    /// <summary>Время создания в UTC.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>Время обработки в UTC или null для ожидающей брони.</summary>
    public DateTime? ProcessedAt { get; set; }

    /// <summary>Преобразовать доменную сущность в ответ API.</summary>
    public static BookingInfo FromBooking(Booking booking) => new()
    {
        Id = booking.Id,
        EventId = booking.EventId,
        Status = booking.Status,
        CreatedAt = booking.CreatedAt,
        ProcessedAt = booking.ProcessedAt
    };
}
