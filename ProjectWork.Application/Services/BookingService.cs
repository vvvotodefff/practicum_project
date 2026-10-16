using ProjectWork.Application.Abstractions;
using ProjectWork.Application.Abstractions.Repositories;
using ProjectWork.Application.DTO;
using ProjectWork.Domain.Exceptions;
using ProjectWork.Domain.Entities;

namespace ProjectWork.Application.Services;

/// <summary>Прикладные операции с бронированиями и количеством свободных мест.</summary>
/// <param name="eventRepository">Доступ к событиям в текущем scope.</param>
/// <param name="bookingRepository">Доступ к бронированиям в том же scope.</param>
/// <param name="unitOfWork">Атомарное сохранение брони и изменения мест.</param>
/// <param name="userRepository">Проверка существования владельца.</param>
/// <param name="timeProvider">Текущее время для проверки начала события.</param>
public class BookingService(IEventRepository eventRepository, IBookingRepository bookingRepository,
    IUnitOfWork unitOfWork, IUserRepository userRepository, TimeProvider timeProvider) : IBookingService
{
    /// <summary>Лимит активных (Pending и Confirmed) броней одного пользователя.</summary>
    public const int MaxActiveBookings = 10;

    /// <inheritdoc />
    public async Task<List<BookingInfo>> GetBookingsAsync(CancellationToken cancellationToken = default) =>
        (await bookingRepository.GetAllAsync(cancellationToken: cancellationToken))
            .Select(BookingInfo.FromBooking).ToList();

    /// <inheritdoc />
    public async Task<BookingInfo?> GetBookingByIdAsync(Guid bookingId, CancellationToken cancellationToken = default)
    {
        var booking = await bookingRepository.GetByIdAsync(bookingId, cancellationToken);
        return booking is null ? null : BookingInfo.FromBooking(booking);
    }

    /// <inheritdoc />
    public async Task<List<BookingInfo>> GetPendingBookingsAsync(CancellationToken cancellationToken = default) =>
        (await bookingRepository.GetAllAsync(BookingStatus.Pending, cancellationToken))
            .Select(BookingInfo.FromBooking).ToList();

    /// <inheritdoc />
    public async Task<BookingInfo> CreateBookingAsync(Guid eventId, Guid userId, CancellationToken cancellationToken = default)
    {
        await EventWriteLock.Gate.WaitAsync(cancellationToken);
        try
        {
            var eventItem = await eventRepository.GetByIdForUpdateAsync(eventId, cancellationToken)
                ?? throw new NotFoundException($"Событие с идентификатором '{eventId}' не найдено.");
            if (eventItem.StartAt <= timeProvider.GetUtcNow().UtcDateTime)
                throw new EventAlreadyStartedException("Нельзя забронировать событие, которое уже началось.");
            if (await userRepository.GetByIdAsync(userId, cancellationToken) is null)
                throw new NotFoundException("Пользователь не найден.");
            if (await bookingRepository.CountActiveByUserAsync(userId, cancellationToken) >= MaxActiveBookings)
                throw new ActiveBookingLimitExceededException($"Лимит активных бронирований: {MaxActiveBookings}.");
            if (!eventItem.TryReserveSeats())
                throw new NoAvailableSeatsException("No available seats for this event");

            var booking = Booking.Create(eventId, userId);
            // Одна единица работы сохраняет бронь и уменьшение мест атомарно.
            await bookingRepository.AddAsync(booking, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return BookingInfo.FromBooking(booking);
        }
        finally { EventWriteLock.Gate.Release(); }
    }

    /// <inheritdoc />
    public async Task CancelBookingAsync(Guid bookingId, Guid userId, UserRole role,
        CancellationToken cancellationToken = default)
    {
        await EventWriteLock.Gate.WaitAsync(cancellationToken);
        try
        {
            var booking = await bookingRepository.GetByIdForUpdateAsync(bookingId, cancellationToken)
                ?? throw new NotFoundException("Бронь не найдена.");
            // Права проверяются до изменения статуса, даже при повторной отмене.
            if (booking.UserId != userId && role != UserRole.Admin)
                throw new ForbiddenOperationException("Можно отменить только свою бронь.");

            if (!booking.Cancel()) return;
            var eventItem = await eventRepository.GetByIdForUpdateAsync(booking.EventId, cancellationToken)
                ?? throw new NotFoundException("Событие не найдено.");
            eventItem.ReleaseSeats();
            await bookingRepository.UpdateAsync(booking, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        finally { EventWriteLock.Gate.Release(); }
    }

    /// <inheritdoc />
    public async Task<bool> MarkAsProcessedAsync(Guid bookingId, BookingStatus status,
        CancellationToken cancellationToken = default)
    {
        if (status is not (BookingStatus.Confirmed or BookingStatus.Rejected)) return false;
        await EventWriteLock.Gate.WaitAsync(cancellationToken);
        try
        {
            var booking = await bookingRepository.GetByIdForUpdateAsync(bookingId, cancellationToken);
            if (booking is null) return false;
            // Повторная обработка не должна второй раз возвращать место.
            if (booking.Status != BookingStatus.Pending) return false;

            if (status == BookingStatus.Rejected)
            {
                var eventItem = await eventRepository.GetByIdForUpdateAsync(booking.EventId, cancellationToken)
                    ?? throw new NotFoundException($"Событие с идентификатором '{booking.EventId}' не найдено.");
                booking.Reject();
                eventItem.ReleaseSeats();
            }
            else
            {
                booking.Confirm();
            }

            await bookingRepository.UpdateAsync(booking, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return true;
        }
        finally { EventWriteLock.Gate.Release(); }
    }
}
