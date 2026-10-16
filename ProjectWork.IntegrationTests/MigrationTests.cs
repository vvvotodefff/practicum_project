using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using ProjectWork.Infrastructure;
using ProjectWork.Infrastructure.Persistence;
using ProjectWork.Domain.Entities;

namespace ProjectWork.IntegrationTests;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class MigrationTests(PostgresFixture fixture) : DatabaseTest(fixture)
{
    [Fact]
    public async Task MigrateAsync_CreatesTablesAndRecordsInitialMigration()
    {
        // Arrange / Act: DatabaseTest.InitializeAsync применил миграции на пустой БД.
        var tables = await Context.Database.SqlQueryRaw<string>("""
            SELECT table_name AS "Value"
            FROM information_schema.tables
            WHERE table_schema = 'public' AND table_type = 'BASE TABLE'
            """).ToListAsync();
        var applied = await Context.Database.GetAppliedMigrationsAsync();

        // Assert
        Assert.Equal(new[] { "__EFMigrationsHistory", "bookings", "events", "users" },
            tables.Order(StringComparer.Ordinal));
        Assert.Contains("20261006154025_InitialCreate", applied);
        Assert.Empty(await Context.Database.GetPendingMigrationsAsync());
        Assert.False(Context.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task MigrateAsync_RepeatedCall_PreservesData()
    {
        // Arrange
        var eventItem = NewEvent();
        await SeedEventsAsync(eventItem);
        var booking = NewBooking(eventItem.Id);
        Context.Bookings.Add(booking);
        await Context.SaveChangesAsync();
        var appliedBefore = (await Context.Database.GetAppliedMigrationsAsync()).ToArray();

        // Act
        await using (var startupScope = NewScope())
            await startupScope.ServiceProvider.MigrateDatabaseAsync();

        // Assert
        await using var scope = NewScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(eventItem.Id, (await context.Events.SingleAsync()).Id);
        Assert.Equal(booking.Id, (await context.Bookings.SingleAsync()).Id);
        Assert.Equal(appliedBefore, await context.Database.GetAppliedMigrationsAsync());
    }

    [Theory]
    [InlineData("events", new string[] { "id", "title", "start_at", "end_at", "total_seats", "available_seats" })]
    [InlineData("bookings", new string[] { "id", "event_id", "user_id", "status", "created_at" })]
    public async Task Migration_ConfiguresRequiredColumns(string tableName, string[] expectedColumns)
    {
        // Act
        var columns = await Context.Database.SqlQuery<string>($"""
            SELECT column_name AS "Value"
            FROM information_schema.columns
            WHERE table_schema = 'public' AND table_name = {tableName} AND is_nullable = 'NO'
            """).ToListAsync();

        // Assert
        Assert.Equal(expectedColumns.Order(StringComparer.Ordinal), columns.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Migration_ConfiguresForeignKeyAndDatabaseCascadeDelete()
    {
        // Arrange
        var eventItem = NewEvent();
        await SeedEventsAsync(eventItem);
        Context.Bookings.Add(NewBooking(eventItem.Id));
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();
        var definition = await Context.Database.SqlQueryRaw<string>("""
            SELECT pg_get_constraintdef(oid) AS "Value"
            FROM pg_constraint
            WHERE conrelid = 'bookings'::regclass AND conname = 'FK_bookings_events_event_id'
            """).SingleAsync();
        Assert.Equal("FOREIGN KEY (event_id) REFERENCES events(id) ON DELETE CASCADE", definition);

        // Act: прямой SQL проверяет каскад PostgreSQL, а не удаление зависимостей средствами EF.
        await Context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM events WHERE id = {eventItem.Id}");

        // Assert
        await using var scope = NewScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Empty(await context.Events.ToListAsync());
        Assert.Empty(await context.Bookings.ToListAsync());
    }

    [Fact]
    public async Task Migration_RejectsBookingWithoutExistingEvent()
    {
        // Arrange
        Context.Bookings.Add(NewBooking(Guid.NewGuid()));

        // Act / Assert
        await AssertPostgresErrorAsync(() => Context.SaveChangesAsync(),
            PostgresErrorCodes.ForeignKeyViolation, "FK_bookings_events_event_id");
    }

    [Theory]
    [InlineData(BookingStatus.Pending)]
    [InlineData(BookingStatus.Confirmed)]
    [InlineData(BookingStatus.Rejected)]
    [InlineData(BookingStatus.Cancelled)]
    public async Task Migration_StoresBookingStatusAsString(BookingStatus status)
    {
        // Arrange
        var eventItem = NewEvent();
        await SeedEventsAsync(eventItem);
        var booking = NewBooking(eventItem.Id, status);

        // Act
        await Bookings.AddAsync(booking);
        await UnitOfWork.SaveChangesAsync();

        // Assert: читаем SQL-значение напрямую, без обратного преобразования EF.
        var stored = await Context.Database.SqlQuery<string>($"""
            SELECT status AS "Value" FROM bookings WHERE id = {booking.Id}
            """).SingleAsync();
        Assert.Equal(status.ToString(), stored);
        var maxLength = await Context.Database.SqlQueryRaw<int>("""
            SELECT character_maximum_length AS "Value"
            FROM information_schema.columns
            WHERE table_schema = 'public' AND table_name = 'bookings' AND column_name = 'status'
            """).SingleAsync();
        Assert.Equal(20, maxLength);
    }

    [Theory]
    [InlineData("missing_title", PostgresErrorCodes.NotNullViolation, null)]
    [InlineData("long_title", PostgresErrorCodes.StringDataRightTruncation, null)]
    [InlineData("long_description", PostgresErrorCodes.StringDataRightTruncation, null)]
    [InlineData("equal_dates", PostgresErrorCodes.CheckViolation, "ck_events_dates")]
    [InlineData("reversed_dates", PostgresErrorCodes.CheckViolation, "ck_events_dates")]
    [InlineData("zero_seats", PostgresErrorCodes.CheckViolation, "ck_events_seats")]
    [InlineData("negative_available", PostgresErrorCodes.CheckViolation, "ck_events_seats")]
    [InlineData("too_many_available", PostgresErrorCodes.CheckViolation, "ck_events_seats")]
    public async Task Migration_EnforcesEventConstraints(string invalidCase, string sqlState,
        string? constraintName)
    {
        // Arrange: обходим доменную валидацию, чтобы проверить именно ограничения БД.
        var eventItem = NewEvent();
        switch (invalidCase)
        {
            case "missing_title": eventItem.Title = null!; break;
            case "long_title": eventItem.Title = new string('a', 201); break;
            case "long_description": eventItem.Description = new string('a', 2001); break;
            case "equal_dates": eventItem.EndAt = eventItem.StartAt; break;
            case "reversed_dates": eventItem.EndAt = eventItem.StartAt.AddHours(-1); break;
            case "zero_seats": eventItem.TotalSeats = 0; eventItem.AvailableSeats = 0; break;
            case "negative_available": eventItem.AvailableSeats = -1; break;
            case "too_many_available": eventItem.AvailableSeats = eventItem.TotalSeats + 1; break;
            default: throw new ArgumentOutOfRangeException(nameof(invalidCase));
        }

        // Act / Assert
        await AssertPostgresErrorAsync(async () =>
        {
            await Events.AddAsync(eventItem);
            await UnitOfWork.SaveChangesAsync();
        }, sqlState, constraintName);
    }

    [Fact]
    public async Task Migration_RejectsDuplicateEventId()
    {
        // Arrange
        var existing = NewEvent();
        await SeedEventsAsync(existing);
        var duplicate = NewEvent("Duplicate");
        duplicate.Id = existing.Id;

        // Act / Assert
        await AssertPostgresErrorAsync(async () =>
        {
            await Events.AddAsync(duplicate);
            await UnitOfWork.SaveChangesAsync();
        },
            PostgresErrorCodes.UniqueViolation, "PK_events");
    }

    [Fact]
    public async Task Migration_RejectsDuplicateBookingId()
    {
        // Arrange
        var eventItem = NewEvent();
        await SeedEventsAsync(eventItem);
        var existing = NewBooking(eventItem.Id);
        Context.Bookings.Add(existing);
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();
        var duplicate = NewBooking(eventItem.Id);
        duplicate.Id = existing.Id;

        // Act / Assert
        await AssertPostgresErrorAsync(async () =>
        {
            await Bookings.AddAsync(duplicate);
            await UnitOfWork.SaveChangesAsync();
        },
            PostgresErrorCodes.UniqueViolation, "PK_bookings");
    }
}
