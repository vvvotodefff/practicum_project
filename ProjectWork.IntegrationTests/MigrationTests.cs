using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using ProjectWork.Users.Infrastructure.Persistence;
using ProjectWork.Events.Infrastructure.Persistence;
using ProjectWork.Bookings.Infrastructure.Persistence;
using ProjectWork.Bookings.Domain.Entities;

namespace ProjectWork.IntegrationTests;

[Collection(PostgresCollection.Name)]
public class MigrationTests(PostgresFixture fixture)
{
    [Theory]
    [InlineData("Users", 1, "users")]
    [InlineData("Events", 2, "events", "processed_bookings")]
    [InlineData("Bookings", 1, "bookings")]
    public async Task Migrations_CreateOnlyOwnedTablesAndMatchModel(string service, int migrations, params string[] tables)
    {
        await using var scope = fixture.CreateScope();
        DbContext context = service switch
        {
            "Users" => scope.ServiceProvider.GetRequiredService<UsersDbContext>(),
            "Events" => scope.ServiceProvider.GetRequiredService<EventsDbContext>(),
            _ => scope.ServiceProvider.GetRequiredService<BookingsDbContext>()
        };
        await context.Database.EnsureDeletedAsync();
        await context.Database.MigrateAsync();
        Assert.Equal(migrations, (await context.Database.GetAppliedMigrationsAsync()).Count());
        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
        Assert.False(context.Database.HasPendingModelChanges());
        var actualTables = await context.Database.SqlQueryRaw<string>(
            """SELECT table_name AS "Value" FROM information_schema.tables WHERE table_schema = 'public' AND table_name <> '__EFMigrationsHistory'""").ToListAsync();
        Assert.Equal(tables.Order(), actualTables.Order());
        // Проверяем Down и повторное применение миграции.
        await context.GetService<IMigrator>().MigrateAsync("0");
        Assert.Empty(await context.Database.GetAppliedMigrationsAsync());
        await context.Database.MigrateAsync();
        Assert.Equal(migrations, (await context.Database.GetAppliedMigrationsAsync()).Count());
    }

    [Fact]
    public async Task BookingDatabase_PersistsIdsAndStatesWithoutUserOrEventTables()
    {
        await fixture.ResetAsync<BookingsDbContext>();
        await using var scope = fixture.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<BookingsDbContext>();
        var userId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var pending = Booking.Create(eventId, userId);
        var confirmed = Booking.Create(eventId, userId); confirmed.Confirm();
        var rejected = Booking.Create(eventId, userId); rejected.Reject();
        var cancelled = Booking.Create(eventId, userId); cancelled.Cancel();
        context.Bookings.AddRange(pending, confirmed, rejected, cancelled);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var repository = scope.ServiceProvider.GetRequiredService<ProjectWork.Bookings.Application.Abstractions.Repositories.IBookingRepository>();
        Assert.Equal(2, await repository.CountActiveByUserAsync(userId));
        Assert.Equal(4, await context.Bookings.CountAsync(b => b.EventId == eventId && b.UserId == userId));
        Assert.Empty(context.Model.FindEntityType(typeof(Booking))!.GetForeignKeys());
    }
}
