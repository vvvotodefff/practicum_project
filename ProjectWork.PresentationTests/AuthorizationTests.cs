using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ProjectWork.Users.Infrastructure.Security;
using ProjectWork.Users.Domain.Entities;

namespace ProjectWork.PresentationTests;

[Collection("HTTP API")]
public class AuthorizationTests(HttpApiFixture fixture)
{
    private async Task<string> LoginAsync(string role = "User")
    {
        using var users = fixture.Users();
        var login = "user-" + Guid.NewGuid().ToString("N");
        var registered = await users.PostAsJsonAsync("/auth/register", new { login, password = "password", role });
        Assert.Equal(HttpStatusCode.NoContent, registered.StatusCode);
        var response = await users.PostAsJsonAsync("/auth/login", new { login, password = "password" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;
    }

    private static void Authorize(HttpClient client, string token) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    private static object NewEvent() => new
    {
        title = "HTTP event", description = "Separated services",
        startAt = DateTime.UtcNow.AddDays(1), endAt = DateTime.UtcNow.AddDays(1).AddHours(2), totalSeats = 5
    };

    [Fact]
    public async Task EachApi_ExposesOnlyItsOwnRoutes()
    {
        using var users = fixture.Users();
        using var events = fixture.Events();
        using var bookings = fixture.Bookings();
        Assert.Equal(HttpStatusCode.NotFound, (await users.GetAsync("/events")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await events.PostAsJsonAsync("/auth/register", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bookings.GetAsync("/events")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await events.GetAsync("/bookings/" + Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await events.GetAsync("/events")).StatusCode);
    }

    [Fact]
    public async Task TokenFromUsers_IsAcceptedByOtherApisAndRolesAreEnforced()
    {
        using var events = fixture.Events();
        using var bookings = fixture.Bookings();
        Assert.Equal(HttpStatusCode.Unauthorized, (await events.PostAsJsonAsync("/events", NewEvent())).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await bookings.PostAsync($"/events/{Guid.NewGuid()}/book", null)).StatusCode);
        var token = await LoginAsync();
        Authorize(events, token);
        Authorize(bookings, token);
        Assert.Equal(HttpStatusCode.Forbidden, (await events.PostAsJsonAsync("/events", NewEvent())).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, (await bookings.PostAsync($"/events/{Guid.NewGuid()}/book", null)).StatusCode);
        Authorize(events, await LoginAsync("Admin"));
        var created = await events.PostAsJsonAsync("/events", NewEvent());
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.NotNull(created.Headers.Location);
    }

    [Fact]
    public async Task Booking_OwnershipAndLifecycleRemainProtected()
    {
        using var bookings = fixture.Bookings();
        var owner = await LoginAsync();
        var other = await LoginAsync();
        Authorize(bookings, owner);
        var created = await bookings.PostAsync($"/events/{Guid.NewGuid()}/book", null);
        Assert.Equal(HttpStatusCode.Accepted, created.StatusCode);
        var location = created.Headers.Location!;
        var body = await created.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Pending", body.GetProperty("status").GetString());
        Authorize(bookings, other);
        Assert.Equal(HttpStatusCode.NotFound, (await bookings.GetAsync(location)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await bookings.DeleteAsync(location)).StatusCode);
        Authorize(bookings, owner);
        Assert.Equal(HttpStatusCode.OK, (await bookings.GetAsync(location)).StatusCode);
        Authorize(bookings, await LoginAsync("Admin"));
        Assert.Equal(HttpStatusCode.OK, (await bookings.GetAsync(location)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await bookings.DeleteAsync(location)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await bookings.DeleteAsync(location)).StatusCode);
        var cancelled = await bookings.GetFromJsonAsync<JsonElement>(location);
        Assert.Equal("Cancelled", cancelled.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Services_DoNotModifyEachOthersDataBeforeKafkaIsConnected()
    {
        using var events = fixture.Events();
        using var bookings = fixture.Bookings();
        Authorize(events, await LoginAsync("Admin"));
        Authorize(bookings, await LoginAsync());
        var created = await events.PostAsJsonAsync("/events", NewEvent());
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var eventBody = await created.Content.ReadFromJsonAsync<JsonElement>();
        var eventId = eventBody.GetProperty("id").GetGuid();
        var accepted = await bookings.PostAsync($"/events/{eventId}/book", null);
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        var location = accepted.Headers.Location!;
        JsonElement booking = default;
        for (var attempt = 0; attempt < 100; attempt++)
        {
            booking = await bookings.GetFromJsonAsync<JsonElement>(location);
            if (booking.GetProperty("status").GetString() == "Confirmed") break;
            await Task.Delay(100);
        }
        Assert.Equal("Confirmed", booking.GetProperty("status").GetString());
        var unchanged = await events.GetFromJsonAsync<JsonElement>($"/events/{eventId}");
        Assert.Equal(5, unchanged.GetProperty("availableSeats").GetInt32());
        Assert.Equal(HttpStatusCode.NoContent, (await events.DeleteAsync($"/events/{eventId}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await bookings.GetAsync(location)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await bookings.DeleteAsync(location)).StatusCode);
    }

    [Fact]
    public async Task Events_ValidationCrudAndMissingResources()
    {
        using var events = fixture.Events();
        Authorize(events, await LoginAsync("Admin"));
        Assert.Equal(HttpStatusCode.BadRequest, (await events.PostAsJsonAsync("/events", new { title = "" })).StatusCode);
        var created = await events.PostAsJsonAsync("/events", NewEvent());
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var update = await events.PutAsJsonAsync($"/events/{id}", new
        {
            title = "Updated", startAt = DateTime.UtcNow.AddDays(2), endAt = DateTime.UtcNow.AddDays(3), totalSeats = 7
        });
        Assert.Equal(HttpStatusCode.NoContent, update.StatusCode);
        Assert.Equal("Updated", (await events.GetFromJsonAsync<JsonElement>($"/events/{id}")).GetProperty("title").GetString());
        Assert.Equal(HttpStatusCode.NoContent, (await events.DeleteAsync($"/events/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await events.GetAsync($"/events/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await events.PutAsJsonAsync($"/events/{id}", NewEvent())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await events.DeleteAsync($"/events/{id}")).StatusCode);
    }

    [Fact]
    public async Task RegistrationAndLogin_InvalidDataReturnsBadRequest()
    {
        using var users = fixture.Users();
        Assert.Equal(HttpStatusCode.BadRequest, (await users.PostAsJsonAsync("/auth/register", new { login = "", password = "" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await users.PostAsJsonAsync("/auth/register", new { login = "role", password = "password", role = "Unknown" })).StatusCode);
        var login = Guid.NewGuid().ToString();
        Assert.Equal(HttpStatusCode.NoContent, (await users.PostAsJsonAsync("/auth/register", new { login, password = "password" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await users.PostAsJsonAsync("/auth/register", new { login, password = "password" })).StatusCode);
        var wrong = await users.PostAsJsonAsync("/auth/login", new { login, password = "wrong" });
        var missing = await users.PostAsJsonAsync("/auth/login", new { login = Guid.NewGuid().ToString(), password = "wrong" });
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Equal((await wrong.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail").GetString(),
            (await missing.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail").GetString());
    }

    [Theory]
    [InlineData("wrong-key")]
    [InlineData("wrong-issuer")]
    [InlineData("wrong-audience")]
    [InlineData("expired")]
    [InlineData("malformed")]
    public async Task InvalidJwt_IsRejectedByBothProtectedApis(string invalid)
    {
        var options = new JwtOptions(
            invalid == "wrong-key" ? HttpApiFixture.Secret + "wrong" : HttpApiFixture.Secret,
            invalid == "wrong-issuer" ? "wrong" : "http-tests",
            invalid == "wrong-audience" ? "wrong" : "http-tests", 60);
        var token = new JwtTokenGenerator(options, invalid == "expired" ? new OldClock() : TimeProvider.System)
            .Generate(User.Create("invalid", "hash", UserRole.Admin));
        if (invalid == "malformed") token = "not-a-jwt";
        using var events = fixture.Events();
        using var bookings = fixture.Bookings();
        Authorize(events, token); Authorize(bookings, token);
        Assert.Equal(HttpStatusCode.Unauthorized, (await events.PostAsJsonAsync("/events", NewEvent())).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await bookings.GetAsync($"/bookings/{Guid.NewGuid()}")).StatusCode);
    }

    private sealed class OldClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow.AddDays(-2);
    }

    [Fact]
    public async Task Swagger_SplitsRoutesAndMarksOnlyAuthorizedOperations()
    {
        using var users = fixture.Users();
        using var events = fixture.Events();
        using var bookings = fixture.Bookings();
        var u = (await users.GetFromJsonAsync<JsonElement>("/swagger/v1/swagger.json")).GetProperty("paths");
        var e = (await events.GetFromJsonAsync<JsonElement>("/swagger/v1/swagger.json")).GetProperty("paths");
        var b = (await bookings.GetFromJsonAsync<JsonElement>("/swagger/v1/swagger.json")).GetProperty("paths");
        Assert.Equal(2, u.EnumerateObject().Count());
        Assert.Equal(3, e.EnumerateObject().Count());
        Assert.Equal(2, b.EnumerateObject().Count());
        Assert.False(u.GetProperty("/auth/login").GetProperty("post").TryGetProperty("security", out _));
        Assert.False(e.GetProperty("/events").GetProperty("get").TryGetProperty("security", out _));
        Assert.True(e.GetProperty("/events").GetProperty("post").TryGetProperty("security", out _));
        Assert.False(e.GetProperty("/events/top").GetProperty("get").TryGetProperty("security", out _));
        Assert.True(b.GetProperty("/bookings/{id}").GetProperty("get").TryGetProperty("security", out _));
    }
}
