using System.Collections.Concurrent;
using ProjectWork.Bookings.Application.Abstractions.Messaging;
using ProjectWork.Contracts;

namespace ProjectWork.Tests;

internal sealed class RecordingBookingEventPublisher : IBookingEventPublisher
{
    public ConcurrentQueue<BookingConfirmed> Messages { get; } = new();
    public Func<BookingConfirmed, CancellationToken, Task>? BeforePublish { get; set; }

    public async Task PublishConfirmedAsync(BookingConfirmed message, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (BeforePublish is not null) await BeforePublish(message, cancellationToken);
        Messages.Enqueue(message);
    }
}
