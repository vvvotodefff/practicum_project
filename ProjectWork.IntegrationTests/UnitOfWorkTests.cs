using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectWork.Infrastructure.Persistence;

namespace ProjectWork.IntegrationTests;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class UnitOfWorkTests(PostgresFixture fixture) : DatabaseTest(fixture)
{
    [Fact]
    public async Task SaveChangesAsync_CommitsChangesFromBothRepositoriesOnlyWhenCalled()
    {
        var eventItem = NewEvent(seats: 3);
        Assert.True(eventItem.TryReserveSeats());
        var booking = NewBooking(eventItem.Id);

        await Events.AddAsync(eventItem);
        await Bookings.AddAsync(booking);

        await using (var before = NewScope())
        {
            var context = before.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Empty(await context.Events.ToListAsync());
            Assert.Empty(await context.Bookings.ToListAsync());
        }

        await UnitOfWork.SaveChangesAsync();

        await using var after = NewScope();
        var saved = after.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(2, (await saved.Events.SingleAsync()).AvailableSeats);
        Assert.Equal(booking.Id, (await saved.Bookings.SingleAsync()).Id);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UpdateAndDelete_DoNotPersistUntilUnitOfWorkSaves(bool delete)
    {
        var eventItem = NewEvent("Original", seats: 3);
        await SeedEventsAsync(eventItem);

        if (delete)
        {
            Assert.True(await Events.DeleteAsync(eventItem.Id));
        }
        else
        {
            var tracked = await Events.GetByIdForUpdateAsync(eventItem.Id);
            tracked!.Update("Updated", null, At(1), At(1, 12), 4);
            await Events.UpdateAsync(tracked);
        }

        await using (var before = NewScope())
        {
            var context = before.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal("Original", (await context.Events.SingleAsync()).Title);
        }

        await UnitOfWork.SaveChangesAsync();

        await using var after = NewScope();
        var saved = after.ServiceProvider.GetRequiredService<AppDbContext>();
        if (delete)
            Assert.Empty(await saved.Events.ToListAsync());
        else
            Assert.Equal("Updated", (await saved.Events.SingleAsync()).Title);
    }
}
