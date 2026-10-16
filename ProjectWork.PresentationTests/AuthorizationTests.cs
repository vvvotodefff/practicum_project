using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ProjectWork.Application.DTO;
using ProjectWork.Domain.Entities;
using ProjectWork.Infrastructure.Security;

namespace ProjectWork.PresentationTests;

[Collection("HTTP API")]
public sealed class AuthorizationTests(HttpApiFixture fixture)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private sealed class OffsetClock(TimeSpan offset) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow.Add(offset);
    }

    private static CreateEvent Request(DateTime? start = null, int seats = 30) => new()
    {
        Title = "HTTP-" + Guid.NewGuid(), TotalSeats = seats,
        StartAt = start ?? DateTime.UtcNow.AddDays(30),
        EndAt = (start ?? DateTime.UtcNow.AddDays(30)).AddHours(1)
    };

    private async Task<string> RegisterAndLogin(HttpClient client, UserRole role = UserRole.User)
    {
        var login = "user-" + Guid.NewGuid();
        var registered = await client.PostAsJsonAsync("/auth/register",
            new RegisterUser { Login = login, Password = "password", Role = role });
        Assert.Equal(HttpStatusCode.NoContent, registered.StatusCode);
        var loggedIn = await client.PostAsJsonAsync("/auth/login",
            new LoginUser { Login = login, Password = "password" });
        Assert.Equal(HttpStatusCode.OK, loggedIn.StatusCode);
        return (await loggedIn.Content.ReadFromJsonAsync<TokenInfo>())!.Token;
    }

    private async Task<EventInfo> CreateEvent(HttpClient client, DateTime? start = null, int seats = 30)
    {
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await RegisterAndLogin(client, UserRole.Admin));
        var response = await client.PostAsJsonAsync("/events", Request(start, seats));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<EventInfo>())!;
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    public async Task EventWrites_RequireTokenAndAdminRole(string method)
    {
        using var client = fixture.CreateClient();
        var path = method == "POST" ? "/events" : "/events/" + Guid.NewGuid();
        async Task<HttpResponseMessage> Send() => await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path)
        {
            Content = JsonContent.Create(Request())
        });
        Assert.Equal(HttpStatusCode.Unauthorized, (await Send()).StatusCode);
        client.DefaultRequestHeaders.Authorization = new("Bearer", await RegisterAndLogin(client));
        Assert.Equal(HttpStatusCode.Forbidden, (await Send()).StatusCode);
        client.DefaultRequestHeaders.Authorization = new("Bearer", await RegisterAndLogin(client, UserRole.Admin));
        Assert.Equal(method == "POST" ? HttpStatusCode.Created : HttpStatusCode.NotFound, (await Send()).StatusCode);
    }

    [Theory]
    [InlineData("POST", "/events/00000000-0000-0000-0000-000000000001/book")]
    [InlineData("GET", "/bookings/00000000-0000-0000-0000-000000000001")]
    [InlineData("DELETE", "/bookings/00000000-0000-0000-0000-000000000001")]
    public async Task BookingEndpoints_RequireAuthentication(string method, string path)
    {
        using var client = fixture.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path))).StatusCode);
        client.DefaultRequestHeaders.Authorization = new("Bearer", await RegisterAndLogin(client));
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path))).StatusCode);
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("issuer")]
    [InlineData("audience")]
    [InlineData("signature")]
    [InlineData("malformed")]
    public async Task InvalidToken_IsRejected(string kind)
    {
        using var client = fixture.CreateClient();
        var token = new JwtTokenGenerator(new JwtOptions(
            kind == "signature" ? HttpApiFixture.Secret + "wrong" : HttpApiFixture.Secret,
            kind == "issuer" ? "wrong" : "http-tests",
            kind == "audience" ? "wrong" : "http-tests", 60),
            new OffsetClock(kind == "expired" ? TimeSpan.FromHours(-2) : TimeSpan.Zero))
            .Generate(User.Create("test", "hash"));
        client.DefaultRequestHeaders.Authorization = new("Bearer", kind == "malformed" ? "invalid.token" : token);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/bookings/" + Guid.NewGuid())).StatusCode);
    }

    [Fact]
    public async Task OwnerCancels_AdminCancelsForeign_OrdinaryUserCannot()
    {
        using var client = fixture.CreateClient();
        var ev = await CreateEvent(client);
        var admin = client.DefaultRequestHeaders.Authorization;
        var owner = new AuthenticationHeaderValue("Bearer", await RegisterAndLogin(client));
        client.DefaultRequestHeaders.Authorization = owner;
        var created = await client.PostAsync($"/events/{ev.Id}/book", null);
        Assert.Equal(HttpStatusCode.Accepted, created.StatusCode);
        Assert.NotNull(created.Headers.Location);
        var booking = (await created.Content.ReadFromJsonAsync<BookingInfo>(JsonOptions))!;
        Assert.NotEqual(Guid.Empty, booking.UserId);
        var read = await client.GetAsync(created.Headers.Location);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        client.DefaultRequestHeaders.Authorization = new("Bearer", await RegisterAndLogin(client));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.DeleteAsync($"/bookings/{booking.Id}")).StatusCode);
        client.DefaultRequestHeaders.Authorization = owner;
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/bookings/{booking.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/bookings/{booking.Id}")).StatusCode);
        Assert.Equal(BookingStatus.Cancelled,
            (await client.GetFromJsonAsync<BookingInfo>($"/bookings/{booking.Id}", JsonOptions))!.Status);
        var second = (await (await client.PostAsync($"/events/{ev.Id}/book", null))
            .Content.ReadFromJsonAsync<BookingInfo>(JsonOptions))!;
        client.DefaultRequestHeaders.Authorization = admin;
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/bookings/{second.Id}")).StatusCode);
        Assert.Equal(30, (await client.GetFromJsonAsync<EventInfo>($"/events/{ev.Id}"))!.AvailableSeats);
    }

    [Fact]
    public async Task StartedEvent_Returns400()
    {
        using var client = fixture.CreateClient();
        var ev = await CreateEvent(client, DateTime.UtcNow.AddHours(-1));
        client.DefaultRequestHeaders.Authorization = new("Bearer", await RegisterAndLogin(client));
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync($"/events/{ev.Id}/book", null)).StatusCode);
    }

    [Fact]
    public async Task UserLimit_Returns409_AndAnotherUserCanBook()
    {
        using var client = fixture.CreateClient();
        var ev = await CreateEvent(client);
        client.DefaultRequestHeaders.Authorization = new("Bearer", await RegisterAndLogin(client));
        for (var i = 0; i < 10; i++)
            Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsync($"/events/{ev.Id}/book", null)).StatusCode);
        var rejected = await client.PostAsync($"/events/{ev.Id}/book", null);
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        Assert.Contains("10", await rejected.Content.ReadAsStringAsync());
        client.DefaultRequestHeaders.Authorization = new("Bearer", await RegisterAndLogin(client));
        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsync($"/events/{ev.Id}/book", null)).StatusCode);
    }

    [Fact]
    public async Task RegistrationValidation_AndGenericLoginError()
    {
        using var client = fixture.CreateClient();
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync("/auth/register", new { login = "", password = "" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync("/auth/register", new { login = "invalid", password = "password", role = "Unknown" })).StatusCode);
        var login = "default-" + Guid.NewGuid();
        Assert.Equal(HttpStatusCode.NoContent,
            (await client.PostAsJsonAsync("/auth/register", new { login, password = "password" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync("/auth/register", new { login, password = "password" })).StatusCode);
        var missing = await client.PostAsJsonAsync("/auth/login", new { login = "missing", password = "wrong" });
        var wrong = await client.PostAsJsonAsync("/auth/login", new { login, password = "wrong" });
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        var a = await missing.Content.ReadFromJsonAsync<JsonElement>();
        var b = await wrong.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(a.GetProperty("detail").GetString(), b.GetProperty("detail").GetString());
        var token = (await (await client.PostAsJsonAsync("/auth/login", new { login, password = "password" }))
            .Content.ReadFromJsonAsync<TokenInfo>())!.Token;
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/events", Request())).StatusCode);
    }

    [Fact]
    public async Task Swagger_DefinesBearerAuthentication()
    {
        using var client = fixture.CreateClient();
        var json = await client.GetFromJsonAsync<JsonElement>("/swagger/v1/swagger.json");
        var bearer = json.GetProperty("components").GetProperty("securitySchemes").GetProperty("bearer");
        Assert.Equal("http", bearer.GetProperty("type").GetString());
        Assert.Equal("bearer", bearer.GetProperty("scheme").GetString());
        var paths = json.GetProperty("paths");
        Assert.True(paths.TryGetProperty("/auth/login", out _));
        Assert.True(paths.TryGetProperty("/auth/register", out _));
        Assert.True(paths.GetProperty("/bookings/{id}").TryGetProperty("delete", out _));
    }
}
