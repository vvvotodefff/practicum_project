using ProjectWork.Contracts;

namespace ProjectWork.Bookings.Application.Abstractions.Messaging;

/// <summary>
/// Публикация сообщений о бронированиях
/// </summary>
public interface IBookingEventPublisher
{
    /// <summary>
    /// Опубликовать сообщение о подтверждённой брони
    /// </summary>
    Task PublishConfirmedAsync(BookingConfirmed message, CancellationToken cancellationToken = default);
}