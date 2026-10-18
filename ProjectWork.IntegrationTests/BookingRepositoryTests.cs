using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using ProjectWork.Bookings.Infrastructure.Persistence;
using ProjectWork.Bookings.Application.Abstractions;
using ProjectWork.Bookings.Application.Abstractions.Repositories;
using ProjectWork.Bookings.Domain.Entities;

namespace ProjectWork.IntegrationTests;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class BookingRepositoryTests(PostgresFixture fixture) : BookingsDatabaseTest(fixture)
{
    private async Task<Booking[]> SeedBookingsAsync()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var bookings = new[]
        {
            NewBooking(first),
            NewBooking(first, BookingStatus.Confirmed),
            NewBooking(first, BookingStatus.Rejected),
            NewBooking(second)
        };
        Context.Bookings.AddRange(bookings);
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();
        return bookings;
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
        var saved = await scope.ServiceProvider.GetRequiredService<BookingsDbContext>()
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
        var saved = await scope.ServiceProvider.GetRequiredService<BookingsDbContext>()
            .Bookings.SingleAsync(b => b.Id == original.Id);
        Assert.Equal(original.Id, saved.Id);
        Assert.Equal(original.EventId, saved.EventId);
        Assert.Equal(original.CreatedAt, saved.CreatedAt);
        Assert.Equal(status, saved.Status);
        Assert.NotNull(saved.ProcessedAt);
        Assert.Equal(DateTimeKind.Utc, saved.ProcessedAt.Value.Kind);
    }



    [Fact]
    public async Task UpdateAsync_MissingBooking_ThrowsConcurrencyException()
    {
        // Arrange
        var missing = NewBooking(Guid.NewGuid());

        // Act / Assert
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(async () =>
        {
            await Bookings.UpdateAsync(missing);
            await UnitOfWork.SaveChangesAsync();
        });
        await using var scope = NewScope();
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<BookingsDbContext>().Bookings.ToListAsync());
    }
}
