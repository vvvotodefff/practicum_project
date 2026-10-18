namespace ProjectWork.Events.Infrastructure.Persistence;

/// <summary>
/// Отметка о том, что подтверждение брони уже применено к событию
/// </summary>
public sealed class ProcessedBooking
{
    public Guid BookingId { get; init; }

    public DateTime ProcessedAt { get; init; }
}
