using ProjectWork.Bookings.Application.Abstractions;
using ProjectWork.Bookings.Application.Abstractions.Messaging;
using ProjectWork.Contracts;
using ProjectWork.Bookings.Application.Abstractions.Repositories;
using ProjectWork.Bookings.Application.DTO;
using ProjectWork.Bookings.Domain.Exceptions;
using ProjectWork.Bookings.Domain.Entities;

namespace ProjectWork.Bookings.Application.Services;

/// <summary>Операции с бронями в собственной базе; внешние сущности представлены идентификаторами.</summary>
public class BookingService(IBookingRepository bookingRepository, IUnitOfWork unitOfWork,
    IBookingEventPublisher eventPublisher) : IBookingService
{
    /// <summary>Лимит активных (Pending и Confirmed) броней одного пользователя.</summary>
    public const int MaxActiveBookings = 10;

    /// <inheritdoc />
    public async Task<List<BookingInfo>> GetBookingsAsync(CancellationToken cancellationToken = default) =>
        (await bookingRepository.GetAllAsync(cancellationToken: cancellationToken))
            .Select(BookingInfo.FromBooking).ToList();

    /// <inheritdoc />
    public async Task<BookingInfo?> GetBookingByIdAsync(Guid bookingId, Guid userId, bool isAdmin, CancellationToken cancellationToken = default)
    {
        var booking = await bookingRepository.GetByIdAsync(bookingId, cancellationToken);
        return booking is null || (booking.UserId != userId && !isAdmin)
            ? null : BookingInfo.FromBooking(booking);
    }

    /// <inheritdoc />
    public async Task<List<BookingInfo>> GetPendingBookingsAsync(CancellationToken cancellationToken = default) =>
        (await bookingRepository.GetAllAsync(BookingStatus.Pending, cancellationToken))
            .Select(BookingInfo.FromBooking).ToList();

    /// <inheritdoc />
    public async Task<BookingInfo> CreateBookingAsync(Guid eventId, Guid userId, CancellationToken cancellationToken = default)
    {
        await BookingWriteLock.Gate.WaitAsync(cancellationToken);
        try
        {
            if (await bookingRepository.CountActiveByUserAsync(userId, cancellationToken) >= MaxActiveBookings)
                throw new ActiveBookingLimitExceededException($"Лимит активных бронирований: {MaxActiveBookings}.");
            var booking = Booking.Create(eventId, userId);
            // Сохраняется только бронь. События и пользователи принадлежат другим сервисам.
            await bookingRepository.AddAsync(booking, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return BookingInfo.FromBooking(booking);
        }
        finally { BookingWriteLock.Gate.Release(); }
    }

    /// <inheritdoc />
    public async Task CancelBookingAsync(Guid bookingId, Guid userId, bool isAdmin,
        CancellationToken cancellationToken = default)
    {
        await BookingWriteLock.Gate.WaitAsync(cancellationToken);
        try
        {
            var booking = await bookingRepository.GetByIdForUpdateAsync(bookingId, cancellationToken)
                ?? throw new NotFoundException("Бронь не найдена.");
            // Права проверяются до изменения статуса, даже при повторной отмене.
            if (booking.UserId != userId && !isAdmin)
                throw new ForbiddenOperationException("Можно отменить только свою бронь.");

            if (!booking.Cancel()) return;
            await bookingRepository.UpdateAsync(booking, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        finally { BookingWriteLock.Gate.Release(); }
    }

    /// <inheritdoc />
    public async Task<bool> MarkAsProcessedAsync(Guid bookingId, BookingStatus status,
        CancellationToken cancellationToken = default)
    {
        if (status is not (BookingStatus.Confirmed or BookingStatus.Rejected)) return false;
        BookingConfirmed? message = null;
        await BookingWriteLock.Gate.WaitAsync(cancellationToken);
        try
        {
            var booking = await bookingRepository.GetByIdForUpdateAsync(bookingId, cancellationToken);
            if (booking is null) return false;
            // Повторная обработка не меняет конечный статус.
            if (booking.Status != BookingStatus.Pending) return false;

            if (status == BookingStatus.Rejected)
            {
                booking.Reject();
            }
            else
            {
                booking.Confirm();
            }

            await bookingRepository.UpdateAsync(booking, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            // Сообщение описывает подтверждение, успешно сохранённое в своей БД.
            if (status == BookingStatus.Confirmed)
                message = new BookingConfirmed(booking.Id, booking.EventId, booking.UserId,
                    SeatsCount: 1, ConfirmedAt: new DateTimeOffset(booking.ProcessedAt!.Value));
        }
        finally { BookingWriteLock.Gate.Release(); }

        // Сеть не удерживает общую блокировку операций с бронями.
        // Ошибка публикации не отменяет сохранённый Confirmed (Outbox пока не реализован).
        if (message is not null)
            await eventPublisher.PublishConfirmedAsync(message, cancellationToken);
        return true;
    }
}
