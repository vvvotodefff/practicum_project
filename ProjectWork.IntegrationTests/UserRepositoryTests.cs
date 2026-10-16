using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using ProjectWork.Application.Abstractions.Repositories;
using ProjectWork.Domain.Entities;
using ProjectWork.Domain.Exceptions;
using ProjectWork.Infrastructure.Persistence;

namespace ProjectWork.IntegrationTests;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class UserRepositoryTests(PostgresFixture fixture) : DatabaseTest(fixture)
{
    private IUserRepository Users => new ProjectWork.Infrastructure.Repositories.UserRepository(Context);

    [Fact]
    public async Task AddAndRead_PersistsFieldsAndReturnsUntrackedUser()
    {
        var user = User.Create("admin", new string('B', 64), UserRole.Admin);
        await Users.AddAsync(user);
        await UnitOfWork.SaveChangesAsync();
        Context.ChangeTracker.Clear();
        var saved = await Users.GetByLoginAsync("admin");
        Assert.NotNull(saved);
        Assert.Equal(user.Id, saved.Id);
        Assert.Equal(user.PasswordHash, saved.PasswordHash);
        Assert.Equal(UserRole.Admin, saved.Role);
        Assert.Equal(user.Id, (await Users.GetByIdAsync(user.Id))!.Id);
        Assert.Null(await Users.GetByLoginAsync("missing"));
        Assert.Null(await Users.GetByIdAsync(Guid.NewGuid()));
        Assert.Empty(Context.ChangeTracker.Entries());
    }

    [Fact]
    public async Task UniqueLogin_IsEnforcedAcrossScopes()
    {
        await Users.AddAsync(User.Create("duplicate", new string('A', 64)));
        await UnitOfWork.SaveChangesAsync();
        await using var scope = NewScope();
        var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        await users.AddAsync(User.Create("duplicate", new string('B', 64)));
        var work = scope.ServiceProvider.GetRequiredService<ProjectWork.Application.Abstractions.IUnitOfWork>();
        await Assert.ThrowsAsync<DomainValidationException>(() => work.SaveChangesAsync());
        Assert.Equal(1, await Context.Users.CountAsync(u => u.Login == "duplicate"));
    }

    [Fact]
    public async Task BookingWithoutUser_IsRejectedByForeignKey()
    {
        var ev = NewEvent();
        await SeedEventsAsync(ev);
        Context.Bookings.Add(Booking.Create(ev.Id, Guid.NewGuid()));
        await AssertPostgresErrorAsync(() => Context.SaveChangesAsync(),
            Npgsql.PostgresErrorCodes.ForeignKeyViolation, "FK_bookings_users_user_id");
    }

    [Fact]
    public async Task CountActiveByUser_IncludesOnlyPendingAndConfirmed()
    {
        var ev = NewEvent();
        await SeedEventsAsync(ev);
        var other = User.Create("other", new string('A', 64));
        Context.Users.Add(other);
        Context.Bookings.AddRange(NewBooking(ev.Id), NewBooking(ev.Id, BookingStatus.Confirmed),
            NewBooking(ev.Id, BookingStatus.Cancelled), NewBooking(ev.Id, BookingStatus.Rejected),
            Booking.Create(ev.Id, other.Id));
        await Context.SaveChangesAsync();
        Assert.Equal(2, await Bookings.CountActiveByUserAsync(UserId));
        Assert.Equal(1, await Bookings.CountActiveByUserAsync(other.Id));
    }

    [Fact]
    public async Task Migration_UpgradesExistingBookingsWithoutLosingData()
    {
        Context.ChangeTracker.Clear();
        var migrator = Context.GetService<IMigrator>();
        await migrator.MigrateAsync("20261006154025_InitialCreate");
        var eventId = Guid.NewGuid();
        var bookingId = Guid.NewGuid();
        await Context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO events (id, title, start_at, end_at, total_seats, available_seats)
            VALUES ({eventId}, 'Legacy', {At(1)}, {At(1, 12)}, 2, 1);
            """);
        await Context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO bookings (id, event_id, status, created_at)
            VALUES ({bookingId}, {eventId}, 'Pending', {At(1)});
            """);
        await migrator.MigrateAsync();

        var booking = await Context.Bookings.AsNoTracking().SingleAsync();
        Assert.Equal(bookingId, booking.Id);
        Assert.Equal(eventId, booking.EventId);
        Assert.Equal(1, (await Context.Events.SingleAsync()).AvailableSeats);
        var owner = await Context.Users.SingleAsync(u => u.Id == booking.UserId);
        Assert.StartsWith("__legacy_bookings_", owner.Login);
        Assert.Equal(64, owner.PasswordHash.Length);
        Assert.False(Context.Database.HasPendingModelChanges());
    }
}
