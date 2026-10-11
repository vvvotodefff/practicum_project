using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using ProjectWork.Infrastructure.Persistence;
using ProjectWork.Application.Abstractions;
using ProjectWork.Application.Abstractions.Repositories;
using ProjectWork.Domain.Entities;

namespace ProjectWork.IntegrationTests;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class BookingRepositoryTests(PostgresFixture fixture) : DatabaseTest(fixture)
{
    private async Task<Booking[]> SeedBookingsAsync()
    {
        var first = NewEvent("First");
        var second = NewEvent("Second");
        await SeedEventsAsync(first, second);
        var bookings = new[]
        {
            NewBooking(first.Id),
            NewBooking(first.Id, BookingStatus.Confirmed),
            NewBooking(first.Id, BookingStatus.Rejected),
            NewBooking(second.Id)
        };
        Context.Bookings.AddRange(bookings);
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();
        return bookings;
    }

    [Fact]
    public async Task AddAsync_PersistsBookingAndReservedSeatInSharedContext()
    {
        // Arrange
        var eventItem = NewEvent(seats: 3);
        await SeedEventsAsync(eventItem);
        var tracked = await Events.GetByIdForUpdateAsync(eventItem.Id);
        Assert.True(tracked!.TryReserveSeats());
        var booking = NewBooking(eventItem.Id);
        var originalId = booking.Id;

        // Act
        await Bookings.AddAsync(booking);
        await UnitOfWork.SaveChangesAsync();

        // Assert
        await using var scope = NewScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var saved = await context.Bookings.SingleAsync();
        Assert.Equal(originalId, saved.Id);
        Assert.Equal(eventItem.Id, saved.EventId);
        Assert.Equal(BookingStatus.Pending, saved.Status);
        Assert.Equal(booking.CreatedAt, saved.CreatedAt);
        Assert.Equal(DateTimeKind.Utc, saved.CreatedAt.Kind);
        Assert.Null(saved.ProcessedAt);
        Assert.Equal(2, (await context.Events.SingleAsync()).AvailableSeats);
    }

    [Fact]
    public async Task AddAsync_FailedInsert_RollsBackSeatChange()
    {
        // Arrange: намеренно нарушаем FK, чтобы проверить транзакцию настоящей PostgreSQL.
        var eventItem = NewEvent(seats: 3);
        await SeedEventsAsync(eventItem);
        var tracked = await Events.GetByIdForUpdateAsync(eventItem.Id);
        Assert.True(tracked!.TryReserveSeats());
        var invalidBooking = NewBooking(Guid.NewGuid());

        // Act / Assert
        await AssertPostgresErrorAsync(async () =>
        {
            await Bookings.AddAsync(invalidBooking);
            await UnitOfWork.SaveChangesAsync();
        },
            PostgresErrorCodes.ForeignKeyViolation, "FK_bookings_events_event_id");
        await using var scope = NewScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(3, (await context.Events.SingleAsync()).AvailableSeats);
        Assert.Empty(await context.Bookings.ToListAsync());
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsUntrackedBooking()
    {
        // Arrange
        var expected = (await SeedBookingsAsync())[0];

        // Act
        var actual = await Bookings.GetByIdAsync(expected.Id);

        // Assert
        Assert.NotNull(actual);
        Assert.Equal(expected.Id, actual.Id);
        Assert.Equal(expected.EventId, actual.EventId);
        Assert.Equal(expected.Status, actual.Status);
        Assert.Equal(expected.CreatedAt, actual.CreatedAt);
        Assert.Null(actual.ProcessedAt);
        Assert.Empty(Context.ChangeTracker.Entries());
        actual.Confirm();
        await Context.SaveChangesAsync();
        await using var scope = NewScope();
        var saved = await scope.ServiceProvider.GetRequiredService<AppDbContext>()
            .Bookings.SingleAsync(b => b.Id == expected.Id);
        Assert.Equal(BookingStatus.Pending, saved.Status);
    }

    [Fact]
    public async Task GetByIdAsync_AndGetByIdForUpdateAsync_ReturnNullForMissingBooking()
    {
        // Arrange
        var id = Guid.NewGuid();

        // Act / Assert
        Assert.Null(await Bookings.GetByIdAsync(id));
        Assert.Null(await Bookings.GetByIdForUpdateAsync(id));
    }

    [Fact]
    public async Task GetByIdForUpdateAsync_TracksAndRefreshesStateFromAnotherScope()
    {
        // Arrange
        var expected = (await SeedBookingsAsync())[0];
        var tracked = await Bookings.GetByIdForUpdateAsync(expected.Id);
        Assert.NotNull(tracked);
        Assert.Equal(EntityState.Unchanged, Context.Entry(tracked).State);
        await using (var scope = NewScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<IBookingRepository>();
            var updated = await repository.GetByIdForUpdateAsync(expected.Id);
            updated!.Confirm();
            await repository.UpdateAsync(updated);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        }

        // Act
        var refreshed = await Bookings.GetByIdForUpdateAsync(expected.Id);

        // Assert
        Assert.Same(tracked, refreshed);
        Assert.Equal(BookingStatus.Confirmed, refreshed!.Status);
        Assert.NotNull(refreshed.ProcessedAt);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(BookingStatus.Pending)]
    [InlineData(BookingStatus.Confirmed)]
    [InlineData(BookingStatus.Rejected)]
    [InlineData((BookingStatus)999)]
    public async Task GetAllAsync_AppliesOptionalStatusFilterWithoutTracking(BookingStatus? status)
    {
        // Arrange
        var bookings = await SeedBookingsAsync();
        var expected = bookings.Where(b => !status.HasValue || b.Status == status.Value);

        // Act
        var result = await Bookings.GetAllAsync(status);

        // Assert
        Assert.Equal(expected.Select(b => b.Id).Order(), result.Select(b => b.Id).Order());
        Assert.Empty(Context.ChangeTracker.Entries());
    }

    [Theory]
    [InlineData(BookingStatus.Pending)]
    [InlineData(BookingStatus.Confirmed)]
    [InlineData(BookingStatus.Rejected)]
    [InlineData((BookingStatus)999)]
    public async Task GetIdsByStatusAsync_ReturnsOnlyMatchingIdsWithoutTracking(BookingStatus status)
    {
        // Arrange
        var bookings = await SeedBookingsAsync();

        // Act
        var ids = await Bookings.GetIdsByStatusAsync(status);

        // Assert
        Assert.Equal(bookings.Where(b => b.Status == status).Select(b => b.Id).Order(), ids.Order());
        Assert.Empty(Context.ChangeTracker.Entries());
    }

    [Fact]
    public async Task GetAllAsync_AndGetIdsByStatusAsync_EmptyDatabase_ReturnEmptyLists()
    {
        // Act / Assert
        Assert.Empty(await Bookings.GetAllAsync());
        Assert.Empty(await Bookings.GetAllAsync(BookingStatus.Pending));
        Assert.Empty(await Bookings.GetIdsByStatusAsync(BookingStatus.Pending));
    }

    [Theory]
    [InlineData(true, BookingStatus.Confirmed)]
    [InlineData(false, BookingStatus.Confirmed)]
    [InlineData(true, BookingStatus.Rejected)]
    [InlineData(false, BookingStatus.Rejected)]
    public async Task UpdateAsync_PersistsTrackedOrDetachedBooking(bool tracked, BookingStatus status)
    {
        // Arrange
        var original = (await SeedBookingsAsync())[0];
        var updated = tracked
            ? await Bookings.GetByIdForUpdateAsync(original.Id)
            : await Bookings.GetByIdAsync(original.Id);
        if (status == BookingStatus.Confirmed)
            updated!.Confirm();
        else
            updated!.Reject();

        // Act
        await Bookings.UpdateAsync(updated);
        await UnitOfWork.SaveChangesAsync();

        // Assert
        await using var scope = NewScope();
        var saved = await scope.ServiceProvider.GetRequiredService<AppDbContext>()
            .Bookings.SingleAsync(b => b.Id == original.Id);
        Assert.Equal(original.Id, saved.Id);
        Assert.Equal(original.EventId, saved.EventId);
        Assert.Equal(original.CreatedAt, saved.CreatedAt);
        Assert.Equal(status, saved.Status);
        Assert.NotNull(saved.ProcessedAt);
        Assert.Equal(DateTimeKind.Utc, saved.ProcessedAt.Value.Kind);
    }

    [Fact]
    public async Task UpdateAsync_PersistsRejectedStatusAndReleasedSeatTogether()
    {
        // Arrange
        var eventItem = NewEvent(seats: 2);
        Assert.True(eventItem.TryReserveSeats());
        await SeedEventsAsync(eventItem);
        var booking = NewBooking(eventItem.Id);
        Context.Bookings.Add(booking);
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();
        var trackedEvent = await Events.GetByIdForUpdateAsync(eventItem.Id);
        var trackedBooking = await Bookings.GetByIdForUpdateAsync(booking.Id);
        trackedEvent!.ReleaseSeats();
        trackedBooking!.Reject();

        // Act
        await Bookings.UpdateAsync(trackedBooking);
        await UnitOfWork.SaveChangesAsync();

        // Assert
        await using var scope = NewScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(BookingStatus.Rejected, (await context.Bookings.SingleAsync()).Status);
        Assert.Equal(2, (await context.Events.SingleAsync()).AvailableSeats);
    }

    [Fact]
    public async Task UpdateAsync_FailedUpdate_RollsBackOtherTrackedChanges()
    {
        // Arrange
        var eventItem = NewEvent(seats: 2);
        Assert.True(eventItem.TryReserveSeats());
        await SeedEventsAsync(eventItem);
        var booking = NewBooking(eventItem.Id);
        Context.Bookings.Add(booking);
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();
        var trackedEvent = await Events.GetByIdForUpdateAsync(eventItem.Id);
        var trackedBooking = await Bookings.GetByIdForUpdateAsync(booking.Id);
        trackedEvent!.ReleaseSeats();
        trackedBooking!.Reject();
        trackedBooking.EventId = Guid.NewGuid();

        // Act / Assert
        await AssertPostgresErrorAsync(async () =>
        {
            await Bookings.UpdateAsync(trackedBooking);
            await UnitOfWork.SaveChangesAsync();
        },
            PostgresErrorCodes.ForeignKeyViolation, "FK_bookings_events_event_id");
        await using var scope = NewScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var saved = await context.Bookings.SingleAsync();
        Assert.Equal(eventItem.Id, saved.EventId);
        Assert.Equal(BookingStatus.Pending, saved.Status);
        Assert.Null(saved.ProcessedAt);
        Assert.Equal(1, (await context.Events.SingleAsync()).AvailableSeats);
    }

    [Fact]
    public async Task UpdateAsync_MissingBooking_ThrowsConcurrencyException()
    {
        // Arrange
        var eventItem = NewEvent();
        await SeedEventsAsync(eventItem);
        var missing = NewBooking(eventItem.Id);

        // Act / Assert
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(async () =>
        {
            await Bookings.UpdateAsync(missing);
            await UnitOfWork.SaveChangesAsync();
        });
        await using var scope = NewScope();
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<AppDbContext>().Bookings.ToListAsync());
    }
}
