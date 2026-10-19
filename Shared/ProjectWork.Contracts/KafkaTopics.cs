namespace ProjectWork.Contracts;

/// <summary>
/// Имена топиков, общие для издателей и подписчиков
/// </summary>
public static class KafkaTopics
{
    /// <summary>
    /// Сообщения <see cref="BookingConfirmed"/> о подтверждении брони
    /// </summary>
    public const string BookingConfirmed = "booking-confirmed";
}
