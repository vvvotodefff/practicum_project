namespace ProjectWork.Events.Infrastructure.Messaging;

/// <summary>
/// Настройки подключения к Kafka
/// </summary>
public sealed class KafkaOptions
{
    public string BootstrapServers { get; }

    /// <summary>
    /// Группа потребителей: внутри группы сообщение получает один экземпляр сервиса
    /// </summary>
    public string ConsumerGroup { get; }

    public KafkaOptions(string? bootstrapServers, string? consumerGroup)
    {
        if (string.IsNullOrWhiteSpace(bootstrapServers))
        {
            throw new InvalidOperationException(
                "Не задана настройка Kafka:BootstrapServers.");
        }

        if (string.IsNullOrWhiteSpace(consumerGroup))
        {
            throw new InvalidOperationException(
                "Не задана настройка Kafka:ConsumerGroup.");
        }

        BootstrapServers = bootstrapServers;
        ConsumerGroup = consumerGroup;
    }
}
