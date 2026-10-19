using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectWork.Bookings.Application.Abstractions;
using ProjectWork.Bookings.Application.Abstractions.Repositories;
using ProjectWork.Bookings.Application.Services;
using ProjectWork.Bookings.Domain.Entities;
using ProjectWork.Bookings.Infrastructure.Persistence;
using ProjectWork.Bookings.Infrastructure.Messaging;

namespace ProjectWork.Tests;

public class BookingPublicationTests
{
    [Fact]
    public async Task Confirmation_PublishesSavedFieldsOnceAfterCommit()
    {
        using var database = new TestDatabase();
        using var scope = database.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IBookingService>();
        var publisher = database.Provider.GetRequiredService<RecordingBookingEventPublisher>();
        var created = await service.CreateBookingAsync(Guid.NewGuid(), database.UserId);
        Assert.Empty(publisher.Messages);
        publisher.BeforePublish = async (message, token) =>
        {
            using var readScope = database.CreateScope();
            var context = readScope.ServiceProvider.GetRequiredService<BookingsDbContext>();
            var saved = await context.Bookings.AsNoTracking().SingleAsync(b => b.Id == message.BookingId, token);
            Assert.Equal(BookingStatus.Confirmed, saved.Status);
            Assert.Equal(saved.Id, message.BookingId);
            Assert.Equal(saved.EventId, message.EventId);
            Assert.Equal(saved.UserId, message.UserId);
            Assert.Equal(1, message.SeatsCount);
            Assert.Equal(saved.ProcessedAt, message.ConfirmedAt.UtcDateTime);
            Assert.Equal(TimeSpan.Zero, message.ConfirmedAt.Offset);
        };

        Assert.True(await service.MarkAsProcessedAsync(created.Id, BookingStatus.Confirmed));
        Assert.False(await service.MarkAsProcessedAsync(created.Id, BookingStatus.Confirmed));
        Assert.Single(publisher.Messages);
    }

    [Theory]
    [InlineData(BookingStatus.Rejected)]
    [InlineData(BookingStatus.Cancelled)]
    [InlineData(BookingStatus.Pending)]
    public async Task NonConfirmation_DoesNotPublish(BookingStatus status)
    {
        using var database = new TestDatabase();
        using var scope = database.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IBookingService>();
        var created = await service.CreateBookingAsync(Guid.NewGuid(), database.UserId);
        if (status == BookingStatus.Cancelled)
        {
            await service.CancelBookingAsync(created.Id, database.UserId, false);
            Assert.False(await service.MarkAsProcessedAsync(created.Id, BookingStatus.Confirmed));
        }
        else
        {
            Assert.Equal(status == BookingStatus.Rejected, await service.MarkAsProcessedAsync(created.Id, status));
        }
        Assert.False(await service.MarkAsProcessedAsync(Guid.NewGuid(), BookingStatus.Confirmed));
        Assert.Empty(database.Provider.GetRequiredService<RecordingBookingEventPublisher>().Messages);
    }

    [Fact]
    public async Task SaveFailure_DoesNotCallPublisherOrPersistConfirmed()
    {
        using var database = new TestDatabase();
        using var scope = database.CreateScope();
        var created = await scope.ServiceProvider.GetRequiredService<IBookingService>()
            .CreateBookingAsync(Guid.NewGuid(), database.UserId);
        var publisher = database.Provider.GetRequiredService<RecordingBookingEventPublisher>();
        var service = new BookingService(scope.ServiceProvider.GetRequiredService<IBookingRepository>(),
            new FailingUnitOfWork(), publisher);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.MarkAsProcessedAsync(created.Id, BookingStatus.Confirmed));
        Assert.Empty(publisher.Messages);
        using var readScope = database.CreateScope();
        var saved = await readScope.ServiceProvider.GetRequiredService<BookingsDbContext>().Bookings.SingleAsync();
        Assert.Equal(BookingStatus.Pending, saved.Status);
        Assert.Null(saved.ProcessedAt);
    }

    [Fact]
    public async Task PublishFailure_PropagatesAndLeavesConfirmedWithoutAutomaticRetry()
    {
        using var database = new TestDatabase();
        using var scope = database.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IBookingService>();
        var publisher = database.Provider.GetRequiredService<RecordingBookingEventPublisher>();
        var attempts = 0;
        publisher.BeforePublish = (_, _) =>
        {
            attempts++;
            throw new InvalidOperationException("Kafka unavailable");
        };
        var created = await service.CreateBookingAsync(Guid.NewGuid(), database.UserId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.MarkAsProcessedAsync(created.Id, BookingStatus.Confirmed));
        using var readScope = database.CreateScope();
        var saved = await readScope.ServiceProvider.GetRequiredService<BookingsDbContext>().Bookings.SingleAsync();
        Assert.Equal(BookingStatus.Confirmed, saved.Status);
        Assert.NotNull(saved.ProcessedAt);
        Assert.False(await service.MarkAsProcessedAsync(created.Id, BookingStatus.Confirmed));
        Assert.Equal(1, attempts);
        Assert.Empty(publisher.Messages);
    }

    [Fact]
    public async Task WaitingForPublisher_DoesNotHoldBookingWriteLock()
    {
        using var database = new TestDatabase();
        using var scope = database.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IBookingService>();
        var created = await service.CreateBookingAsync(Guid.NewGuid(), database.UserId);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        database.Provider.GetRequiredService<RecordingBookingEventPublisher>().BeforePublish = async (_, token) =>
        {
            started.TrySetResult();
            await finish.Task.WaitAsync(token);
        };
        var confirmation = service.MarkAsProcessedAsync(created.Id, BookingStatus.Confirmed);
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            using var otherScope = database.CreateScope();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await otherScope.ServiceProvider.GetRequiredService<IBookingService>()
                .CreateBookingAsync(Guid.NewGuid(), database.OtherUserId, timeout.Token);
        }
        finally
        {
            finish.TrySetResult();
            await confirmation;
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void KafkaOptions_RejectMissingAddress(string? address) =>
        Assert.Throws<InvalidOperationException>(() => new KafkaOptions(address));

    private sealed class FailingUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Database save failed");
    }
}
