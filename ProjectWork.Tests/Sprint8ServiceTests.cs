using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectWork.Application.DTO;
using ProjectWork.Application.Services;
using ProjectWork.Domain.Entities;
using ProjectWork.Domain.Exceptions;
using ProjectWork.Infrastructure.Persistence;
using ProjectWork.Infrastructure.Security;

namespace ProjectWork.Tests;

public class Sprint8ServiceTests : IDisposable
{
    private readonly TestDatabase _database = new();
    private readonly IServiceScope _scope;
    private readonly IBookingService _bookings;
    private readonly IEventService _events;
    private readonly IUserService _users;
    private readonly DateTime _now;

    public Sprint8ServiceTests()
    {
        _scope = _database.CreateScope();
        _bookings = _scope.ServiceProvider.GetRequiredService<IBookingService>();
        _events = _scope.ServiceProvider.GetRequiredService<IEventService>();
        _users = _scope.ServiceProvider.GetRequiredService<IUserService>();
        _now = _scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime;
    }

    public void Dispose() { _scope.Dispose(); _database.Dispose(); }

    private Task<EventInfo> EventAsync(DateTime? start = null) => _events.CreateEventAsync(new CreateEvent
    {
        Title = "Sprint 8", StartAt = start ?? _now.AddDays(1),
        EndAt = (start ?? _now.AddDays(1)).AddHours(1), TotalSeats = 50
    });

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    public async Task StartedEvent_IsRejectedWithoutChangingSeats(int seconds)
    {
        var ev = await EventAsync(_now.AddSeconds(seconds));
        await Assert.ThrowsAsync<EventAlreadyStartedException>(() =>
            _bookings.CreateBookingAsync(ev.Id, _database.UserId));
        Assert.Empty(await _bookings.GetBookingsAsync());
        Assert.Equal(50, (await _events.GetEventByIdAsync(ev.Id))!.AvailableSeats);
    }

    [Fact]
    public async Task Limit_IncludesConfirmedBookingsAndDoesNotConsumeEleventhSeat()
    {
        var ev = await EventAsync();
        for (var i = 0; i < BookingService.MaxActiveBookings; i++)
        {
            var booking = await _bookings.CreateBookingAsync(ev.Id, _database.UserId);
            await _bookings.MarkAsProcessedAsync(booking.Id, BookingStatus.Confirmed);
        }
        var exception = await Assert.ThrowsAsync<ActiveBookingLimitExceededException>(() =>
            _bookings.CreateBookingAsync(ev.Id, _database.UserId));
        Assert.Contains("10", exception.Message);
        Assert.Equal(10, (await _bookings.GetBookingsAsync()).Count);
        Assert.Equal(40, (await _events.GetEventByIdAsync(ev.Id))!.AvailableSeats);
    }

    [Fact]
    public async Task Limit_IsIndependentForDifferentUsers()
    {
        var ev = await EventAsync();
        for (var i = 0; i < 10; i++)
            await _bookings.CreateBookingAsync(ev.Id, _database.UserId);
        var second = await _bookings.CreateBookingAsync(ev.Id, _database.OtherUserId);
        Assert.Equal(_database.OtherUserId, second.UserId);
        Assert.Equal(11, (await _bookings.GetBookingsAsync()).Count);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Cancellation_OwnerOrAdmin_ReturnsSeatOnce(bool confirmed, bool administrator)
    {
        var ev = await EventAsync();
        var booking = await _bookings.CreateBookingAsync(ev.Id, _database.UserId);
        if (confirmed) await _bookings.MarkAsProcessedAsync(booking.Id, BookingStatus.Confirmed);
        var actor = administrator ? _database.OtherUserId : _database.UserId;
        var role = administrator ? UserRole.Admin : UserRole.User;
        await _bookings.CancelBookingAsync(booking.Id, actor, role);
        await _bookings.CancelBookingAsync(booking.Id, actor, role);
        Assert.Equal(BookingStatus.Cancelled, (await _bookings.GetBookingByIdAsync(booking.Id, _database.UserId, UserRole.User))!.Status);
        Assert.Equal(50, (await _events.GetEventByIdAsync(ev.Id))!.AvailableSeats);
        Assert.False(await _bookings.MarkAsProcessedAsync(booking.Id, BookingStatus.Confirmed));
    }

    [Fact]
    public async Task Cancellation_OtherUser_IsForbiddenWithoutChangingState()
    {
        var ev = await EventAsync();
        var booking = await _bookings.CreateBookingAsync(ev.Id, _database.UserId);
        await Assert.ThrowsAsync<ForbiddenOperationException>(() =>
            _bookings.CancelBookingAsync(booking.Id, _database.OtherUserId, UserRole.User));
        Assert.Equal(BookingStatus.Pending, (await _bookings.GetBookingByIdAsync(booking.Id, _database.UserId, UserRole.User))!.Status);
        Assert.Equal(49, (await _events.GetEventByIdAsync(ev.Id))!.AvailableSeats);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancelledOrRejectedBooking_DoesNotCountTowardsLimit(bool reject)
    {
        var ev = await EventAsync();
        var first = await _bookings.CreateBookingAsync(ev.Id, _database.UserId);
        for (var i = 1; i < 10; i++)
            await _bookings.CreateBookingAsync(ev.Id, _database.UserId);
        if (reject) await _bookings.MarkAsProcessedAsync(first.Id, BookingStatus.Rejected);
        else await _bookings.CancelBookingAsync(first.Id, _database.UserId, UserRole.User);
        await _bookings.CreateBookingAsync(ev.Id, _database.UserId);
        Assert.Equal(11, (await _bookings.GetBookingsAsync()).Count);
        Assert.Equal(40, (await _events.GetEventByIdAsync(ev.Id))!.AvailableSeats);
    }

    [Fact]
    public async Task ConcurrentRequests_AcrossEvents_CannotExceedUserLimit()
    {
        var first = await EventAsync();
        var second = await EventAsync();
        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(async i =>
        {
            using var scope = _database.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<IBookingService>();
            try
            {
                await service.CreateBookingAsync(i % 2 == 0 ? first.Id : second.Id, _database.UserId);
                return true;
            }
            catch (ActiveBookingLimitExceededException) { return false; }
        }));
        Assert.Equal(10, results.Count(x => x));
        Assert.Equal(10, (await _bookings.GetBookingsAsync()).Count);
        Assert.Equal(90, (await _events.GetEventByIdAsync(first.Id))!.AvailableSeats +
            (await _events.GetEventByIdAsync(second.Id))!.AvailableSeats);
    }

    [Fact]
    public async Task ConcurrentCancellation_ReturnsOnlyOneSeat()
    {
        var ev = await EventAsync();
        var booking = await _bookings.CreateBookingAsync(ev.Id, _database.UserId);
        await Task.WhenAll(Enumerable.Range(0, 10).Select(async _ =>
        {
            using var scope = _database.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IBookingService>()
                .CancelBookingAsync(booking.Id, _database.UserId, UserRole.User);
        }));
        Assert.Equal(50, (await _events.GetEventByIdAsync(ev.Id))!.AvailableSeats);
    }

    [Fact]
    public async Task Register_StoresHashAndLogin_ReturnsToken()
    {
        await _users.RegisterAsync(new RegisterUser { Login = " admin ", Password = "secret-password", Role = UserRole.Admin });
        using var scope = _database.CreateScope();
        var saved = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.SingleAsync(u => u.Login == "admin");
        Assert.NotEqual("secret-password", saved.PasswordHash);
        Assert.True(new Sha256PasswordHasher().Verify("secret-password", saved.PasswordHash));
        Assert.Equal(UserRole.Admin, saved.Role);
        Assert.Equal(3, (await _users.LoginAsync(new LoginUser { Login = "admin", Password = "secret-password" })).Token.Split('.').Length);
    }

    [Theory]
    [InlineData(UserRole.User, false)]
    [InlineData(UserRole.Admin, true)]
    public async Task ReadBooking_OtherUser_CanReadOnlyAsAdmin(UserRole role, bool allowed)
    {
        var ev = await EventAsync();
        var booking = await _bookings.CreateBookingAsync(ev.Id, _database.UserId);
        var result = await _bookings.GetBookingByIdAsync(booking.Id, _database.OtherUserId, role);
        Assert.Equal(allowed, result is not null);
        if (allowed) Assert.Equal(booking.Id, result!.Id);
        Assert.Null(await _bookings.GetBookingByIdAsync(Guid.NewGuid(), _database.OtherUserId, role));
    }

    [Fact]
    public async Task DuplicateLogin_IsRejected()
    {
        await Assert.ThrowsAsync<DomainValidationException>(() =>
            _users.RegisterAsync(new RegisterUser { Login = " test-user ", Password = "password" }));
    }

    [Fact]
    public async Task Login_UnknownUserAndWrongPassword_ProduceSameMessage()
    {
        var unknown = await Assert.ThrowsAsync<DomainValidationException>(() =>
            _users.LoginAsync(new LoginUser { Login = "missing", Password = "wrong" }));
        var wrong = await Assert.ThrowsAsync<DomainValidationException>(() =>
            _users.LoginAsync(new LoginUser { Login = "test-user", Password = "wrong" }));
        Assert.Equal(unknown.Message, wrong.Message);
    }
}
