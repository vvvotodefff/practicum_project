using ProjectWork.Domain.Entities;
using ProjectWork.Domain.Exceptions;

namespace ProjectWork.Tests;

public class Sprint8DomainTests
{
    [Fact]
    public void UserCreate_DefaultRole_AssignsIdAndNormalizesLogin()
    {
        var user = User.Create("  alex  ", "password-hash");

        Assert.NotEqual(Guid.Empty, user.Id);
        Assert.Equal("alex", user.Login);
        Assert.Equal("password-hash", user.PasswordHash);
        Assert.Equal(UserRole.User, user.Role);
        Assert.NotEqual(user.Id, User.Create("alex", "password-hash").Id);
    }

    [Theory]
    [InlineData(UserRole.User)]
    [InlineData(UserRole.Admin)]
    public void UserCreate_AcceptsDefinedRole(UserRole role)
    {
        Assert.Equal(role, User.Create("alex", "hash", role).Role);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void UserCreate_EmptyLogin_Throws(string? login)
    {
        Assert.Throws<DomainValidationException>(() => User.Create(login!, "hash"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void UserCreate_EmptyPasswordHash_Throws(string? passwordHash)
    {
        Assert.Throws<DomainValidationException>(() => User.Create("alex", passwordHash!));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    public void UserCreate_UnknownRole_Throws(int role)
    {
        Assert.Throws<DomainValidationException>(() =>
            User.Create("alex", "hash", (UserRole)role));
    }

    [Fact]
    public void Booking_StateCannotBeChangedThroughPublicSetters()
    {
        Assert.All(typeof(Booking).GetProperties(), property =>
            Assert.False(property.SetMethod?.IsPublic ?? false, property.Name));
        Assert.Empty(typeof(Booking).GetConstructors());
    }

    [Fact]
    public void BookingCreate_AssignsOwnerAndInitialState()
    {
        var eventId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var before = DateTime.UtcNow;

        var booking = Booking.Create(eventId, userId);

        Assert.NotEqual(Guid.Empty, booking.Id);
        Assert.Equal(eventId, booking.EventId);
        Assert.Equal(userId, booking.UserId);
        Assert.Equal(BookingStatus.Pending, booking.Status);
        Assert.InRange(booking.CreatedAt, before, DateTime.UtcNow);
        Assert.Equal(DateTimeKind.Utc, booking.CreatedAt.Kind);
        Assert.Null(booking.ProcessedAt);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void BookingCreate_EmptyEventOrUserId_Throws(bool emptyEvent)
    {
        var eventId = emptyEvent ? Guid.Empty : Guid.NewGuid();
        var userId = emptyEvent ? Guid.NewGuid() : Guid.Empty;

        Assert.Throws<DomainValidationException>(() => Booking.Create(eventId, userId));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Cancel_ActiveBooking_ChangesStatusOnlyOnce(bool confirmed)
    {
        var booking = Booking.Create(Guid.NewGuid(), Guid.NewGuid());
        if (confirmed)
            booking.Confirm();
        var processedAt = booking.ProcessedAt;

        Assert.True(booking.Cancel());
        Assert.Equal(BookingStatus.Cancelled, booking.Status);
        Assert.False(booking.Cancel());
        Assert.Equal(BookingStatus.Cancelled, booking.Status);
        Assert.Equal(processedAt, booking.ProcessedAt);
    }

    [Fact]
    public void Cancel_RejectedBooking_DoesNotChangeState()
    {
        var booking = Booking.Create(Guid.NewGuid(), Guid.NewGuid());
        booking.Reject();
        var processedAt = booking.ProcessedAt;

        Assert.False(booking.Cancel());
        Assert.Equal(BookingStatus.Rejected, booking.Status);
        Assert.Equal(processedAt, booking.ProcessedAt);
    }

    [Theory]
    [InlineData(BookingStatus.Confirmed, true)]
    [InlineData(BookingStatus.Confirmed, false)]
    [InlineData(BookingStatus.Rejected, true)]
    [InlineData(BookingStatus.Rejected, false)]
    [InlineData(BookingStatus.Cancelled, true)]
    [InlineData(BookingStatus.Cancelled, false)]
    public void Processing_FinalStatus_CannotBeOverwritten(BookingStatus status, bool confirm)
    {
        var booking = Booking.Create(Guid.NewGuid(), Guid.NewGuid());
        switch (status)
        {
            case BookingStatus.Confirmed: booking.Confirm(); break;
            case BookingStatus.Rejected: booking.Reject(); break;
            case BookingStatus.Cancelled: booking.Cancel(); break;
        }
        var processedAt = booking.ProcessedAt;

        Assert.Throws<DomainValidationException>(() =>
        {
            if (confirm)
                booking.Confirm();
            else
                booking.Reject();
        });
        Assert.Equal(status, booking.Status);
        Assert.Equal(processedAt, booking.ProcessedAt);
    }
}
