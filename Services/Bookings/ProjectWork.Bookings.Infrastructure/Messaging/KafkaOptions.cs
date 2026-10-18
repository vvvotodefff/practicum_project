namespace ProjectWork.Bookings.Infrastructure.Messaging;

/// <summary>
/// Настройки подключения к Kafka
/// </summary>
public sealed class KafkaOptions
{
    public string BootstrapServers { get; }

    public KafkaOptions(string? bootstrapServers)
    {
        if (string.IsNullOrWhiteSpace(bootstrapServers))
        {
            throw new InvalidOperationException(
                "Не задана настройка Kafka:BootstrapServers.");
        }

        BootstrapServers = bootstrapServers;
    }
}