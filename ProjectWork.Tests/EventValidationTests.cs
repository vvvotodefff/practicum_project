using ProjectWork.Domain.Entities;
using ProjectWork.Domain.Exceptions;

namespace ProjectWork.Tests;

// Проверяем доменные правила через публичные операции, а не через DataAnnotations.
public class EventValidationTests
{
    private sealed record Input(string Title, string? Description, DateTime StartAt,
        DateTime EndAt, int TotalSeats);

    private static Input ValidInput() => new("Встреча", "Описание",
        new DateTime(2026, 11, 1, 10, 0, 0, DateTimeKind.Utc),
        new DateTime(2026, 11, 1, 11, 0, 0, DateTimeKind.Utc), 10);

    public static TheoryData<string, string> InvalidCases => new()
    {
        { "null_title", "Название обязательно" },
        { "empty_title", "Название обязательно" },
        { "whitespace_title", "Название обязательно" },
        { "long_title", "Название не должно превышать 200" },
        { "long_description", "Описание не должно превышать 2000" },
        { "missing_start", "Время начала обязательно" },
        { "missing_end", "Время окончания обязательно" },
        { "missing_dates", "Время начала обязательно" },
        { "equal_dates", "Время окончания должно быть позже" },
        { "reversed_dates", "Время окончания должно быть позже" },
        { "zero_seats", "Общее количество мест должно быть положительным" },
        { "negative_seats", "Общее количество мест должно быть положительным" }
    };

    private static Input InvalidInput(string invalidCase)
    {
        var valid = ValidInput();
        return invalidCase switch
        {
            "null_title" => valid with { Title = null! },
            "empty_title" => valid with { Title = "" },
            "whitespace_title" => valid with { Title = "   " },
            "long_title" => valid with { Title = new string('a', 201) },
            "long_description" => valid with { Description = new string('a', 2001) },
            "missing_start" => valid with { StartAt = default },
            "missing_end" => valid with { EndAt = default },
            "missing_dates" => valid with { StartAt = default, EndAt = default },
            "equal_dates" => valid with { EndAt = valid.StartAt },
            "reversed_dates" => valid with { EndAt = valid.StartAt.AddHours(-1) },
            "zero_seats" => valid with { TotalSeats = 0 },
            "negative_seats" => valid with { TotalSeats = -1 },
            _ => throw new ArgumentOutOfRangeException(nameof(invalidCase))
        };
    }

    private static Event Create(Input input) => Event.Create(input.Title, input.Description,
        input.StartAt, input.EndAt, input.TotalSeats);

    [Fact]
    public void Create_ValidData_AssignsIdAndAvailableSeats()
    {
        // Arrange
        var input = ValidInput();

        // Act
        var eventItem = Create(input);

        // Assert
        Assert.NotEqual(Guid.Empty, eventItem.Id);
        Assert.Equal(input.Title, eventItem.Title);
        Assert.Equal(input.Description, eventItem.Description);
        Assert.Equal(input.StartAt, eventItem.StartAt);
        Assert.Equal(input.EndAt, eventItem.EndAt);
        Assert.Equal(DateTimeKind.Utc, eventItem.StartAt.Kind);
        Assert.Equal(input.TotalSeats, eventItem.TotalSeats);
        Assert.Equal(input.TotalSeats, eventItem.AvailableSeats);
    }

    [Theory]
    [MemberData(nameof(InvalidCases))]
    public void Create_InvalidData_ThrowsDomainValidationException(string invalidCase, string message)
    {
        // Arrange
        var input = InvalidInput(invalidCase);

        // Act / Assert
        var exception = Assert.Throws<DomainValidationException>(() => Create(input));
        Assert.Contains(message, exception.Message);
        if (invalidCase == "missing_dates")
            Assert.Contains("Время окончания обязательно", exception.Message);
    }

    [Theory]
    [MemberData(nameof(InvalidCases))]
    public void Update_InvalidData_DoesNotMutateEvent(string invalidCase, string message)
    {
        // Arrange
        var original = ValidInput();
        var eventItem = Create(original);
        var id = eventItem.Id;
        Assert.True(eventItem.TryReserveSeats(3));
        var input = InvalidInput(invalidCase);

        // Act / Assert
        var exception = Assert.Throws<DomainValidationException>(() => eventItem.Update(
            input.Title, input.Description, input.StartAt, input.EndAt, input.TotalSeats));
        Assert.Contains(message, exception.Message);
        if (invalidCase == "missing_dates")
            Assert.Contains("Время окончания обязательно", exception.Message);
        Assert.Equal(id, eventItem.Id);
        Assert.Equal(original.Title, eventItem.Title);
        Assert.Equal(original.Description, eventItem.Description);
        Assert.Equal(original.StartAt, eventItem.StartAt);
        Assert.Equal(original.EndAt, eventItem.EndAt);
        Assert.Equal(original.TotalSeats, eventItem.TotalSeats);
        Assert.Equal(7, eventItem.AvailableSeats);
    }

    [Fact]
    public void Create_MaximumStringLengths_AreAllowed()
    {
        // Arrange
        var input = ValidInput() with { Title = new string('a', 200), Description = new string('b', 2000) };

        // Act
        var eventItem = Create(input);

        // Assert
        Assert.Equal(input.Title, eventItem.Title);
        Assert.Equal(input.Description, eventItem.Description);
    }

    [Fact]
    public void Update_ValidData_PreservesIdAndOccupiedSeats()
    {
        // Arrange
        var eventItem = Create(ValidInput());
        var id = eventItem.Id;
        Assert.True(eventItem.TryReserveSeats(3));
        var input = ValidInput() with
        {
            Title = new string('a', 200), Description = new string('b', 2000), TotalSeats = 20
        };

        // Act
        eventItem.Update(input.Title, input.Description, input.StartAt, input.EndAt, input.TotalSeats);

        // Assert
        Assert.Equal(id, eventItem.Id);
        Assert.Equal(input.Title, eventItem.Title);
        Assert.Equal(input.Description, eventItem.Description);
        Assert.Equal(20, eventItem.TotalSeats);
        Assert.Equal(17, eventItem.AvailableSeats);
    }

    [Fact]
    public void Update_CapacityBelowOccupiedSeats_ThrowsWithoutMutation()
    {
        // Arrange
        var original = ValidInput();
        var eventItem = Create(original);
        Assert.True(eventItem.TryReserveSeats(3));

        // Act / Assert
        Assert.Throws<DomainValidationException>(() => eventItem.Update(
            "Новое название", null, original.StartAt, original.EndAt, 2));
        Assert.Equal(original.Title, eventItem.Title);
        Assert.Equal(original.Description, eventItem.Description);
        Assert.Equal(10, eventItem.TotalSeats);
        Assert.Equal(7, eventItem.AvailableSeats);
    }

    [Theory]
    [InlineData(DateTimeKind.Unspecified)]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Utc)]
    public void ToUtc_NormalizesDateAccordingToKind(DateTimeKind kind)
    {
        // Arrange
        var value = new DateTime(2026, 11, 1, 10, 0, 0, kind);
        var expected = kind == DateTimeKind.Local
            ? value.ToUniversalTime()
            : DateTime.SpecifyKind(value, DateTimeKind.Utc);

        // Act
        var result = Event.ToUtc(value);

        // Assert
        Assert.Equal(expected, result);
        Assert.Equal(DateTimeKind.Utc, result.Kind);
    }
}
