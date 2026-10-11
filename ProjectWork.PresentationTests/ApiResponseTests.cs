using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ProjectWork.Presentation.Controllers;
using ProjectWork.Domain.Entities;
using ProjectWork.Domain.Exceptions;
using ProjectWork.Application.DTO;
using ProjectWork.Presentation.Middleware;
using ProjectWork.Application.Common;
using ProjectWork.Application.Services;

namespace ProjectWork.PresentationTests;

// Удаление JSON-атрибутов из Domain не должно менять публичные ответы API.
public sealed class ApiResponseTests : IDisposable
{
    private readonly TestDatabase _database = new();
    private readonly IServiceScope _scope;
    private readonly IEventService _events;
    private readonly IBookingService _bookings;
    private readonly EventsController _eventsController;
    private readonly BookingController _bookingController;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public ApiResponseTests()
    {
        _scope = _database.CreateScope();
        _events = _scope.ServiceProvider.GetRequiredService<IEventService>();
        _bookings = _scope.ServiceProvider.GetRequiredService<IBookingService>();
        _eventsController = new EventsController(_events);
        _bookingController = new BookingController(_bookings);
    }

    public void Dispose()
    {
        _scope.Dispose();
        _database.Dispose();
    }

    private static CreateEvent Request(string title = "Встреча") => new()
    {
        Title = title, Description = "Описание",
        StartAt = new DateTime(2026, 11, 1, 10, 0, 0, DateTimeKind.Utc),
        EndAt = new DateTime(2026, 11, 1, 11, 0, 0, DateTimeKind.Utc),
        TotalSeats = 3
    };

    private static JsonElement AssertJsonFields(object value, params string[] names)
    {
        var json = JsonSerializer.SerializeToElement(value, JsonOptions);
        Assert.Equal(names.Order(StringComparer.Ordinal),
            json.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        return json;
    }

    private static void AssertEventJson(EventInfo value) => AssertJsonFields(value,
        "id", "title", "description", "startAt", "endAt", "totalSeats", "availableSeats");

    private static JsonElement AssertBookingJson(BookingInfo value) => AssertJsonFields(value,
        "id", "eventId", "status", "createdAt", "processedAt");

    [Fact]
    public async Task CreateEvent_ReturnsDtoAndCreatedLocation()
    {
        // Act
        var result = await _eventsController.CreateEvent(Request());

        // Assert
        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        Assert.Equal(StatusCodes.Status201Created, created.StatusCode);
        var dto = Assert.IsType<EventInfo>(created.Value);
        Assert.Equal(nameof(EventsController.GetEventById), created.ActionName);
        Assert.Equal(dto.Id, created.RouteValues!["id"]);
        Assert.Equal("Встреча", dto.Title);
        Assert.Equal(3, dto.AvailableSeats);
        AssertEventJson(dto);
    }

    [Fact]
    public async Task GetEventById_ReturnsDtoWithoutBookings()
    {
        // Arrange
        var created = await _events.CreateEventAsync(Request());
        await _bookings.CreateBookingAsync(created.Id);

        // Act
        var result = await _eventsController.GetEventById(created.Id);

        // Assert
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<EventInfo>(ok.Value);
        Assert.Equal(created.Id, dto.Id);
        Assert.Equal(created.Description, dto.Description);
        Assert.Equal(created.StartAt, dto.StartAt);
        Assert.Equal(created.EndAt, dto.EndAt);
        Assert.Equal(2, dto.AvailableSeats);
        AssertEventJson(dto);
    }

    [Fact]
    public async Task GetEvents_ReturnsDtoPageAndPreservesPagination()
    {
        // Arrange
        await _events.CreateEventAsync(Request("Встреча 1"));
        await _events.CreateEventAsync(Request("Встреча 2"));
        await _events.CreateEventAsync(Request("Концерт"));

        // Act
        var result = await _eventsController.GetEvents("Встреча", null, null, 2, 1);

        // Assert
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var page = Assert.IsType<PaginatedResult<EventInfo>>(ok.Value);
        Assert.Equal(2, page.TotalCount);
        Assert.Equal(2, page.Page);
        Assert.Equal(1, page.PageSize);
        AssertEventJson(Assert.Single(page.Items));
        AssertJsonFields(page, "totalCount", "page", "pageSize", "items");
    }

    [Fact]
    public async Task BookEvent_ReturnsDtoWithoutEventAndAcceptedLocation()
    {
        // Arrange
        var created = await _events.CreateEventAsync(Request());

        // Act
        var result = await _bookingController.BookEvent(created.Id);

        // Assert
        var accepted = Assert.IsType<AcceptedAtActionResult>(result.Result);
        Assert.Equal(StatusCodes.Status202Accepted, accepted.StatusCode);
        var dto = Assert.IsType<BookingInfo>(accepted.Value);
        Assert.Equal(created.Id, dto.EventId);
        Assert.Equal(BookingStatus.Pending, dto.Status);
        Assert.Null(dto.ProcessedAt);
        Assert.Equal(nameof(BookingController.GetBookingById), accepted.ActionName);
        Assert.Equal(dto.Id, accepted.RouteValues!["id"]);
        Assert.Equal("Pending", AssertBookingJson(dto).GetProperty("status").GetString());
    }

    [Fact]
    public async Task GetBookingById_ReturnsDtoWithProcessedState()
    {
        // Arrange
        var created = await _events.CreateEventAsync(Request());
        var booking = await _bookings.CreateBookingAsync(created.Id);
        Assert.True(await _bookings.MarkAsProcessedAsync(booking.Id, BookingStatus.Confirmed));

        // Act
        var result = await _bookingController.GetBookingById(booking.Id);

        // Assert
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<BookingInfo>(ok.Value);
        Assert.Equal(booking.Id, dto.Id);
        Assert.Equal(created.Id, dto.EventId);
        Assert.Equal(booking.CreatedAt, dto.CreatedAt);
        Assert.NotNull(dto.ProcessedAt);
        Assert.Equal("Confirmed", AssertBookingJson(dto).GetProperty("status").GetString());
    }

    [Theory]
    [InlineData("validation", StatusCodes.Status400BadRequest)]
    [InlineData("missing", StatusCodes.Status404NotFound)]
    [InlineData("seats", StatusCodes.Status409Conflict)]
    [InlineData("annotations", StatusCodes.Status400BadRequest)]
    [InlineData("argument", StatusCodes.Status400BadRequest)]
    public async Task Middleware_MapsKnownExceptionToProblemDetails(string error, int status)
    {
        // Arrange
        Exception exception = error switch
        {
            "validation" => new DomainValidationException("Некорректные даты"),
            "missing" => new NotFoundException("Нет события"),
            "seats" => new NoAvailableSeatsException("Нет мест"),
            "annotations" => new ValidationException("Не заполнено обязательное поле"),
            "argument" => new ArgumentException("Некорректный запрос"),
            _ => throw new ArgumentOutOfRangeException(nameof(error))
        };
        var middleware = new ExceptionHandlingMiddleware(_ => Task.FromException(exception),
            NullLogger<ExceptionHandlingMiddleware>.Instance);
        using var body = new MemoryStream();
        var context = new DefaultHttpContext { RequestServices = _scope.ServiceProvider };
        context.Response.Body = body;

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.Equal(status, context.Response.StatusCode);
        Assert.StartsWith("application/problem+json", context.Response.ContentType);
        body.Position = 0;
        using var json = await JsonDocument.ParseAsync(body);
        Assert.Equal(status, json.RootElement.GetProperty("status").GetInt32());
        Assert.Equal(exception.Message, json.RootElement.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Middleware_UnexpectedExceptionReturns500WithoutInternalDetails()
    {
        var exception = new InvalidOperationException("Database failure: password=do-not-expose");
        var middleware = new ExceptionHandlingMiddleware(_ => Task.FromException(exception),
            NullLogger<ExceptionHandlingMiddleware>.Instance);
        using var body = new MemoryStream();
        var context = new DefaultHttpContext { RequestServices = _scope.ServiceProvider };
        context.Response.Body = body;

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        Assert.StartsWith("application/problem+json", context.Response.ContentType);
        body.Position = 0;
        using var json = await JsonDocument.ParseAsync(body);
        Assert.Equal(500, json.RootElement.GetProperty("status").GetInt32());
        Assert.Equal("Произошла непредвиденная ошибка. Попробуйте позже.",
            json.RootElement.GetProperty("detail").GetString());
        Assert.DoesNotContain(exception.Message, json.RootElement.GetRawText());
    }

    [Fact]
    public async Task Middleware_ClientCancellationIsNotConvertedTo500()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var exception = new OperationCanceledException(cancellation.Token);
        var middleware = new ExceptionHandlingMiddleware(_ => Task.FromException(exception),
            NullLogger<ExceptionHandlingMiddleware>.Instance);
        using var body = new MemoryStream();
        var context = new DefaultHttpContext { RequestAborted = cancellation.Token };
        context.Response.Body = body;

        var actual = await Assert.ThrowsAsync<OperationCanceledException>(() => middleware.InvokeAsync(context));

        Assert.Same(exception, actual);
        Assert.Equal(0, body.Length);
        Assert.Null(context.Response.ContentType);
    }

    [Fact]
    public async Task Middleware_DoesNotOverwriteResponseThatHasAlreadyStarted()
    {
        var exception = new InvalidOperationException("Failure after sending headers");
        var middleware = new ExceptionHandlingMiddleware(_ => Task.FromException(exception),
            NullLogger<ExceptionHandlingMiddleware>.Instance);
        using var body = new MemoryStream();
        var context = new DefaultHttpContext();
        context.Features.Set<IHttpResponseFeature>(new StartedResponseFeature());
        context.Response.Body = body;

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.InvokeAsync(context));

        Assert.Same(exception, actual);
        Assert.Equal(0, body.Length);
        Assert.Null(context.Response.ContentType);
    }

    private sealed class StartedResponseFeature : HttpResponseFeature
    {
        public override bool HasStarted => true;
    }
}
