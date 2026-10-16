using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using ProjectWork.Infrastructure.Persistence;
using ProjectWork.Application.Abstractions;
using ProjectWork.Application.Abstractions.Repositories;
using ProjectWork.Domain.Entities;

namespace ProjectWork.IntegrationTests;

public abstract class DatabaseTest(PostgresFixture fixture) : IAsyncLifetime
{
    private AsyncServiceScope? _scope;
    protected Guid UserId { get; private set; }

    protected AppDbContext Context => Services.GetRequiredService<AppDbContext>();
    protected IUnitOfWork UnitOfWork => Services.GetRequiredService<IUnitOfWork>();
    protected IEventRepository Events => Services.GetRequiredService<IEventRepository>();
    protected IBookingRepository Bookings => Services.GetRequiredService<IBookingRepository>();

    private IServiceProvider Services =>
        _scope?.ServiceProvider ?? throw new InvalidOperationException("Test scope is not initialized.");

    public async Task InitializeAsync()
    {
        // xUnit создаёт новый экземпляр класса и вызывает этот метод для каждого теста,
        // в том числе для каждой строки данных Theory.
        await fixture.ResetDatabaseAsync();
        _scope = fixture.CreateScope();
        var user = User.Create("integration-user", new string('A', 64));
        Context.Users.Add(user);
        await Context.SaveChangesAsync();
        UserId = user.Id;
        Context.ChangeTracker.Clear();
    }

    public async Task DisposeAsync()
    {
        if (_scope is { } scope)
            await scope.DisposeAsync();
    }

    protected AsyncServiceScope NewScope() => fixture.CreateScope();

    protected static DateTime At(int day, int hour = 10) =>
        new(2026, 11, day, hour, 0, 0, DateTimeKind.Utc);

    protected static Event NewEvent(string title = "Test event", int day = 1, int seats = 5) =>
        Event.Create(title, "Integration test", At(day), At(day, 12), seats);

    protected Booking NewBooking(Guid eventId, BookingStatus status = BookingStatus.Pending)
    {
        var booking = Booking.Create(eventId, UserId);
        // PostgreSQL хранит микросекунды, DateTime — 100 нс. Фиксированные даты
        // позволяют сравнивать сохранённые значения без ошибок округления.
        booking.CreatedAt = At(1);
        booking.Status = status;
        booking.ProcessedAt = status == BookingStatus.Pending ? null : At(1, 11);
        return booking;
    }

    protected async Task SeedEventsAsync(params Event[] events)
    {
        Context.Events.AddRange(events);
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();
    }

    protected static async Task AssertPostgresErrorAsync(Func<Task> action, string sqlState,
        string? constraintName = null)
    {
        var exception = await Assert.ThrowsAsync<DbUpdateException>(action);
        var postgres = Assert.IsType<PostgresException>(exception.InnerException);
        Assert.Equal(sqlState, postgres.SqlState);
        if (constraintName is not null)
            Assert.Equal(constraintName, postgres.ConstraintName);
    }
}
