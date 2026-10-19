using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using ProjectWork.Bookings.Application.Abstractions.Messaging;
using ProjectWork.Contracts;

namespace ProjectWork.Bookings.Infrastructure.Messaging;

/// <summary>
/// Отправляет сообщения о подтверждённых бронях в Kafka
/// </summary>
public sealed class KafkaBookingEventPublisher : IBookingEventPublisher, IDisposable
{
    private readonly IProducer<string, string> _producer;
    private readonly ILogger<KafkaBookingEventPublisher> _logger;
    private int _disposed;

    public KafkaBookingEventPublisher(KafkaOptions options, ILogger<KafkaBookingEventPublisher> logger)
    {
        _logger = logger;

        var config = new ProducerConfig
        {
            BootstrapServers = options.BootstrapServers,
            ClientId = "projectwork-bookings",
            Acks = Acks.All,
            EnableIdempotence = true,
            MessageTimeoutMs = 30_000
        };

        _producer = new ProducerBuilder<string, string>(config)
            .Build();
    }

    public async Task PublishConfirmedAsync(
        BookingConfirmed message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        cancellationToken.ThrowIfCancellationRequested();
        var kafkaMessage = new Message<string, string>
        {
            Key = message.EventId.ToString("D"),
            Value = JsonSerializer.Serialize(message)
        };

        var result = await _producer.ProduceAsync(
            KafkaTopics.BookingConfirmed,
            kafkaMessage,
            cancellationToken);

        _logger.LogInformation(
            "BookingConfirmed для брони {BookingId} опубликовано в {Position}",
            message.BookingId,
            result.TopicPartitionOffset);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        try
        {
            var remaining = _producer.Flush(
                TimeSpan.FromSeconds(5));

            if (remaining > 0)
            {
                _logger.LogWarning(
                    "При остановке Kafka producer осталось " +
                    "{Count} незавершённых операций",
                    remaining);
            }
        }
        finally
        {
            _producer.Dispose();
        }
    }
}
