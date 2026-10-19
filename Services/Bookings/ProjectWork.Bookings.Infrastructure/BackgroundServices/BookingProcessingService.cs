using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ProjectWork.Bookings.Application.Abstractions.Repositories;
using ProjectWork.Bookings.Application.Services;
using ProjectWork.Bookings.Domain.Entities;

namespace ProjectWork.Bookings.Infrastructure.BackgroundServices;

/// <summary>Каждая фоновая задача получает собственный scope и DbContext.</summary>
public class BookingProcessingService(
    IServiceScopeFactory scopeFactory,
    ILogger<BookingProcessingService> logger) : BackgroundService
{
    private static readonly TimeSpan ProcessingDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                List<Guid> ids;
                await using (var scope = scopeFactory.CreateAsyncScope())
                {
                    var repository = scope.ServiceProvider.GetRequiredService<IBookingRepository>();
                    ids = await repository.GetIdsByStatusAsync(BookingStatus.Pending, stoppingToken);
                }
                // Контекст чтения уже закрыт; между задачами передаются только Id.
                await Parallel.ForEachAsync(ids,
                    new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = stoppingToken },
                    async (id, token) => await ProcessBookingAsync(id, token));
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Ошибка фоновой обработки бронирований");
            }

            try { await Task.Delay(PollInterval, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    private async Task ProcessBookingAsync(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(ProcessingDelay, cancellationToken);
            await using var scope = scopeFactory.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<IBookingService>();
            if (await service.MarkAsProcessedAsync(id, BookingStatus.Confirmed, cancellationToken))
                logger.LogInformation("Бронь {BookingId} подтверждена, сообщение опубликовано", id);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogInformation("Обработка брони {BookingId} прервана при остановке; " +
                "статус мог быть сохранён до завершения публикации", id);
        }
        catch (Exception ex)
        {
            // Повторно выбираются только Pending. Для Confirmed повторная отправка
            // без отдельного хранилища исходящих сообщений не гарантируется.
            logger.LogError(ex, "Ошибка обработки или публикации брони {BookingId}. " +
                "Если статус уже Confirmed, автоматическая повторная публикация не предусмотрена", id);
        }
    }
}
