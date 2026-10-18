using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ProjectWork.Contracts;

namespace ProjectWork.Events.Infrastructure.Messaging;

/// <summary>
/// Создаёт топик подтверждений броней при старте, чтобы подписчик работал на пустом брокере.
/// Регистрируется раньше подписчика: хост запускает hosted-сервисы по очереди.
/// </summary>
public sealed class KafkaTopicInitializer(KafkaOptions options, ILogger<KafkaTopicInitializer> logger)
    : IHostedService
{
    private const int Partitions = 3;
    private const short ReplicationFactor = 1;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var admin = new AdminClientBuilder(new AdminClientConfig
            {
                BootstrapServers = options.BootstrapServers
            }).Build();

            await admin.CreateTopicsAsync(
                [new TopicSpecification
                {
                    Name = KafkaTopics.BookingConfirmed,
                    NumPartitions = Partitions,
                    ReplicationFactor = ReplicationFactor
                }],
                new CreateTopicsOptions { RequestTimeout = Timeout, OperationTimeout = Timeout })
                .WaitAsync(Timeout, cancellationToken);

            logger.LogInformation("Топик {Topic} создан", KafkaTopics.BookingConfirmed);
        }
        catch (CreateTopicsException ex) when (ex.Results.All(r =>
            r.Error.Code is ErrorCode.NoError or ErrorCode.TopicAlreadyExists))
        {
            logger.LogInformation("Топик {Topic} уже существует", KafkaTopics.BookingConfirmed);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Запуск сервиса не прерываем: топик может создать брокер или администратор.
            logger.LogWarning(ex, "Не удалось создать топик {Topic}", KafkaTopics.BookingConfirmed);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
