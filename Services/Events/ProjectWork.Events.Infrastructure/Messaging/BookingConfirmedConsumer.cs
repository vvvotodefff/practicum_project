using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ProjectWork.Contracts;
using ProjectWork.Events.Application.Services;

namespace ProjectWork.Events.Infrastructure.Messaging;

/// <summary>
/// Читает подтверждения броней из Kafka и занимает места у событий.
/// Сервис singleton, поэтому на каждое сообщение создаётся свой scope с DbContext.
/// </summary>
public sealed class BookingConfirmedConsumer(
    KafkaOptions options,
    IServiceScopeFactory scopeFactory,
    ILogger<BookingConfirmedConsumer> logger) : BackgroundService
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    // Consume блокирует поток, поэтому цикл не должен занимать поток запуска хоста.
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.Run(() => ConsumeLoopAsync(stoppingToken), stoppingToken);

    private async Task ConsumeLoopAsync(CancellationToken stoppingToken)
    {
        var config = new ConsumerConfig
        {
            BootstrapServers = options.BootstrapServers,
            GroupId = options.ConsumerGroup,
            ClientId = "projectwork-events",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            // Смещение сохраняем вручную только после обработки, фиксирует его автокоммит.
            EnableAutoCommit = true,
            EnableAutoOffsetStore = false
        };

        using var consumer = new ConsumerBuilder<string, string>(config).Build();
        consumer.Subscribe(KafkaTopics.BookingConfirmed);
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                ConsumeResult<string, string> result;
                try
                {
                    result = consumer.Consume(stoppingToken);
                }
                catch (ConsumeException ex)
                {
                    logger.LogWarning(ex, "Ошибка чтения из топика {Topic}", KafkaTopics.BookingConfirmed);
                    await Task.Delay(RetryDelay, stoppingToken);
                    continue;
                }

                try
                {
                    await HandleAsync(result, stoppingToken);
                    consumer.StoreOffset(result);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    // Сбой инфраструктуры (например, БД): сообщение не теряем и читаем повторно.
                    // Повтор безопасен благодаря учёту обработанных броней.
                    logger.LogError(ex, "Ошибка обработки сообщения {Position}, повтор через {Delay}",
                        result.TopicPartitionOffset, RetryDelay);
                    TrySeek(consumer, result.TopicPartitionOffset);
                    await Task.Delay(RetryDelay, stoppingToken);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            consumer.Close();
        }
    }

    private async Task HandleAsync(ConsumeResult<string, string> result, CancellationToken cancellationToken)
    {
        var message = Deserialize(result);
        if (message is null) return;

        await using var scope = scopeFactory.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IEventService>();
        var outcome = await service.ApplyBookingConfirmedAsync(
            message.BookingId, message.EventId, message.SeatsCount, cancellationToken);

        switch (outcome)
        {
            case BookingConfirmationResult.Applied:
                logger.LogInformation("Бронь {BookingId}: занято {Seats} мест у события {EventId}",
                    message.BookingId, message.SeatsCount, message.EventId);
                break;
            case BookingConfirmationResult.AlreadyProcessed:
                logger.LogInformation("Бронь {BookingId} уже обработана, повтор пропущен", message.BookingId);
                break;
            case BookingConfirmationResult.EventNotFound:
                logger.LogWarning("Бронь {BookingId} пропущена: событие {EventId} не найдено",
                    message.BookingId, message.EventId);
                break;
            case BookingConfirmationResult.NoAvailableSeats:
                logger.LogWarning("Бронь {BookingId} пропущена: у события {EventId} нет {Seats} свободных мест",
                    message.BookingId, message.EventId, message.SeatsCount);
                break;
        }
    }

    // Некорректное сообщение пропускаем с логированием, чтобы не блокировать раздел.
    private BookingConfirmed? Deserialize(ConsumeResult<string, string> result)
    {
        BookingConfirmed? message;
        try
        {
            message = JsonSerializer.Deserialize<BookingConfirmed>(result.Message.Value ?? "", JsonOptions);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Сообщение {Position} пропущено: некорректный JSON", result.TopicPartitionOffset);
            return null;
        }

        if (message is null || message.BookingId == Guid.Empty || message.EventId == Guid.Empty
            || message.SeatsCount <= 0)
        {
            logger.LogWarning("Сообщение {Position} пропущено: неполные данные брони", result.TopicPartitionOffset);
            return null;
        }

        return message;
    }

    private void TrySeek(IConsumer<string, string> consumer, TopicPartitionOffset position)
    {
        try
        {
            consumer.Seek(position);
        }
        catch (KafkaException ex)
        {
            // Раздел мог перейти другому экземпляру; он прочитает сообщение с последнего смещения.
            logger.LogWarning(ex, "Не удалось вернуться к сообщению {Position}", position);
        }
    }
}
