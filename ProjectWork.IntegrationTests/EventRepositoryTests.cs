using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectWork.Events.Infrastructure.Persistence;
using ProjectWork.Events.Application.Abstractions;
using ProjectWork.Events.Application.Abstractions.Repositories;

namespace ProjectWork.IntegrationTests;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class EventRepositoryTests(PostgresFixture fixture) : EventsDatabaseTest(fixture)
{
    [Fact]
    public async Task AddAsync_PersistsAllFieldsAndClientGeneratedId()
    {
        // Arrange
        var eventItem = NewEvent();
        var originalId = eventItem.Id;

        // Act
        await Events.AddAsync(eventItem);
        await UnitOfWork.SaveChangesAsync();

        // Assert: новый scope исключает получение объекта только из ChangeTracker.
        await using var scope = NewScope();
        var saved = await scope.ServiceProvider.GetRequiredService<EventsDbContext>().Events.SingleAsync();
        Assert.Equal(originalId, saved.Id);
        Assert.Equal(eventItem.Title, saved.Title);
        Assert.Equal(eventItem.Description, saved.Description);
        Assert.Equal(eventItem.StartAt, saved.StartAt);
        Assert.Equal(eventItem.EndAt, saved.EndAt);
        Assert.Equal(DateTimeKind.Utc, saved.StartAt.Kind);
        Assert.Equal(eventItem.TotalSeats, saved.TotalSeats);
        Assert.Equal(eventItem.AvailableSeats, saved.AvailableSeats);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsUntrackedEvent()
    {
        // Arrange
        var eventItem = NewEvent();
        await SeedEventsAsync(eventItem);

        // Act
        var result = await Events.GetByIdAsync(eventItem.Id);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(eventItem.Id, result.Id);
        Assert.Equal(eventItem.Title, result.Title);
        Assert.Empty(Context.ChangeTracker.Entries());
        result.Title = "Not saved";
        await Context.SaveChangesAsync();
        await using var scope = NewScope();
        var saved = await scope.ServiceProvider.GetRequiredService<EventsDbContext>().Events.SingleAsync();
        Assert.Equal(eventItem.Title, saved.Title);
    }

    [Fact]
    public async Task GetByIdAsync_AndGetByIdForUpdateAsync_ReturnNullForMissingEvent()
    {
        // Arrange
        var id = Guid.NewGuid();

        // Act / Assert
        Assert.Null(await Events.GetByIdAsync(id));
        Assert.Null(await Events.GetByIdForUpdateAsync(id));
    }

    [Fact]
    public async Task GetByIdForUpdateAsync_TracksAndRefreshesStateFromAnotherScope()
    {
        // Arrange
        var eventItem = NewEvent();
        await SeedEventsAsync(eventItem);
        var tracked = await Events.GetByIdForUpdateAsync(eventItem.Id);
        Assert.NotNull(tracked);
        Assert.Equal(EntityState.Unchanged, Context.Entry(tracked).State);
        await using (var scope = NewScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<IEventRepository>();
            var updated = await repository.GetByIdForUpdateAsync(eventItem.Id);
            updated!.Update("Changed", "Changed description", At(2), At(2, 12), 8);
            await repository.UpdateAsync(updated);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        }

        // Act
        var refreshed = await Events.GetByIdForUpdateAsync(eventItem.Id);

        // Assert
        Assert.Same(tracked, refreshed);
        Assert.Equal("Changed", refreshed!.Title);
        Assert.Equal(8, refreshed.AvailableSeats);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UpdateAsync_PersistsTrackedOrDetachedEvent(bool tracked)
    {
        // Arrange
        var eventItem = NewEvent();
        await SeedEventsAsync(eventItem);
        var updated = tracked
            ? await Events.GetByIdForUpdateAsync(eventItem.Id)
            : await Events.GetByIdAsync(eventItem.Id);
        updated!.Update("Updated", null, At(2), At(2, 14), 7);

        // Act
        await Events.UpdateAsync(updated);
        await UnitOfWork.SaveChangesAsync();

        // Assert
        await using var scope = NewScope();
        var saved = await scope.ServiceProvider.GetRequiredService<EventsDbContext>().Events.SingleAsync();
        Assert.Equal(eventItem.Id, saved.Id);
        Assert.Equal("Updated", saved.Title);
        Assert.Null(saved.Description);
        Assert.Equal(At(2), saved.StartAt);
        Assert.Equal(At(2, 14), saved.EndAt);
        Assert.Equal(7, saved.TotalSeats);
        Assert.Equal(7, saved.AvailableSeats);
    }

    [Fact]
    public async Task UpdateAsync_MissingEvent_ThrowsConcurrencyException()
    {
        // Arrange
        var missing = NewEvent();

        // Act / Assert
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(async () =>
        {
            await Events.UpdateAsync(missing);
            await UnitOfWork.SaveChangesAsync();
        });
        await using var scope = NewScope();
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<EventsDbContext>().Events.ToListAsync());
    }

    [Fact]
    public async Task DeleteAsync_RemovesOnlySelectedEvent()
    {
        // Arrange
        var removed = NewEvent("Remove");
        var kept = NewEvent("Keep");
        await SeedEventsAsync(removed, kept);
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        // Act
        var result = await Events.DeleteAsync(removed.Id);
        await UnitOfWork.SaveChangesAsync();

        // Assert
        Assert.True(result);
        await using var scope = NewScope();
        var context = scope.ServiceProvider.GetRequiredService<EventsDbContext>();
        Assert.Equal(kept.Id, (await context.Events.SingleAsync()).Id);
        Assert.False(await Events.DeleteAsync(removed.Id));
    }

    [Fact]
    public async Task DeleteAsync_MissingEvent_ReturnsFalse()
    {
        // Arrange
        var id = Guid.NewGuid();

        // Act / Assert
        Assert.False(await Events.DeleteAsync(id));
    }

    [Fact]
    public async Task GetPageAsync_EmptyDatabase_ReturnsEmptyPage()
    {
        // Act
        var result = await Events.GetPageAsync(null, null, null, 1, 10);

        // Assert
        Assert.Equal(0, result.TotalCount);
        Assert.Equal(1, result.Page);
        Assert.Equal(10, result.PageSize);
        Assert.Empty(result.Items);
    }

    public static IEnumerable<object?[]> FilterCases
    {
        get
        {
            string[] all = ["Alpha concert", "BETA", "ALPHA meetup", "Русский концерт"];
            yield return [null, null, null, all];
            yield return ["", null, null, all];
            yield return ["   ", null, null, all];
            yield return ["alphA", null, null, new[] { "Alpha concert", "ALPHA meetup" }];
            yield return ["КОНЦЕРТ", null, null, new[] { "Русский концерт" }];
            yield return ["missing", null, null, Array.Empty<string>()];
            yield return [null, At(2), null, new[] { "BETA", "ALPHA meetup", "Русский концерт" }];
            yield return [null, At(2, 11), null, new[] { "ALPHA meetup", "Русский концерт" }];
            yield return [null, At(10), null, Array.Empty<string>()];
            yield return [null, null, At(3, 12), new[] { "Alpha concert", "BETA", "ALPHA meetup" }];
            yield return [null, null, At(3, 11), new[] { "Alpha concert", "BETA" }];
            yield return [null, null, At(1, 9), Array.Empty<string>()];
            yield return [null, At(2), At(3, 12), new[] { "BETA", "ALPHA meetup" }];
            yield return [null, At(3), At(2, 12), Array.Empty<string>()];
            yield return ["alpha", At(2), null, new[] { "ALPHA meetup" }];
            yield return ["alpha", null, At(2, 12), new[] { "Alpha concert" }];
            yield return ["alpha", At(1), At(3, 12), new[] { "Alpha concert", "ALPHA meetup" }];
            yield return ["beta", At(3), At(4, 12), Array.Empty<string>()];
        }
    }

    [Theory]
    [MemberData(nameof(FilterCases))]
    public async Task GetPageAsync_AppliesEveryFilterCombination(string? title, DateTime? from,
        DateTime? to, string[] expectedTitles)
    {
        // Arrange
        await SeedEventsAsync(NewEvent("Alpha concert", 1), NewEvent("BETA", 2),
            NewEvent("ALPHA meetup", 3), NewEvent("Русский концерт", 4));

        // Act
        var result = await Events.GetPageAsync(title, from, to, 1, 20);

        // Assert
        Assert.Equal(expectedTitles, result.Items.Select(e => e.Title));
        Assert.Equal(expectedTitles.Length, result.TotalCount);
        Assert.Equal(1, result.Page);
        Assert.Equal(20, result.PageSize);
        Assert.Empty(Context.ChangeTracker.Entries());
    }

    [Theory]
    [InlineData(1, 2, new int[] { 1, 2 })]
    [InlineData(2, 2, new int[] { 3, 4 })]
    [InlineData(3, 2, new int[] { 5 })]
    [InlineData(4, 2, new int[] { })]
    [InlineData(1, 20, new int[] { 1, 2, 3, 4, 5 })]
    [InlineData(1, int.MaxValue, new int[] { 1, 2, 3, 4, 5 })]
    [InlineData(int.MaxValue, int.MaxValue, new int[] { })]
    public async Task GetPageAsync_PaginatesWithStableOrderAndTotalCount(int page, int pageSize,
        int[] expectedNumbers)
    {
        // Arrange: одинаковые StartAt проверяют дополнительную сортировку по Id.
        var events = Enumerable.Range(1, 5).Select(number =>
        {
            var eventItem = NewEvent($"Event {number}", (number + 1) / 2);
            eventItem.Id = Guid.Parse($"00000000-0000-0000-0000-{number:D12}");
            return eventItem;
        }).ToArray();
        await SeedEventsAsync(events.Reverse().ToArray());

        // Act
        var result = await Events.GetPageAsync(null, null, null, page, pageSize);

        // Assert
        Assert.Equal(5, result.TotalCount);
        Assert.Equal(page, result.Page);
        Assert.Equal(pageSize, result.PageSize);
        Assert.Equal(expectedNumbers.Select(number => $"Event {number}"), result.Items.Select(e => e.Title));
    }

    [Fact]
    public async Task GetPageAsync_CountsFilteredRowsBeforePagination()
    {
        // Arrange
        await SeedEventsAsync(NewEvent("Alpha concert", 1), NewEvent("BETA", 2),
            NewEvent("ALPHA meetup", 3));

        // Act
        var result = await Events.GetPageAsync("alpha", At(1), At(3, 12), 2, 1);

        // Assert
        Assert.Equal(2, result.TotalCount);
        Assert.Equal("ALPHA meetup", Assert.Single(result.Items).Title);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(-1, 10)]
    [InlineData(1, 0)]
    [InlineData(1, -1)]
    public async Task GetPageAsync_InvalidPagination_Throws(int page, int pageSize)
    {
        // Act / Assert
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            Events.GetPageAsync(null, null, null, page, pageSize));
    }
}
