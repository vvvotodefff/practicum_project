using System.Collections.Concurrent;
using ProjectWork.Bookings.Application.Abstractions.Messaging;
using ProjectWork.Contracts;

namespace ProjectWork.PresentationTests;

internal sealed class RecordingBookingEventPublisher : IBookingEventPublisher
{
    public ConcurrentQueue<BookingConfirmed> Messages { get; } = new();

    public Task PublishConfirmedAsync(BookingConfirmed message, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Messages.Enqueue(message);
        return Task.CompletedTask;
    }
}
