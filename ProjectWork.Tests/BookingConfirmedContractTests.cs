using System.Text.Json;
using ProjectWork.Contracts;

namespace ProjectWork.Tests;

public class BookingConfirmedContractTests
{
    // Независимый пример сообщения: проверяет совместимость JSON с общим контрактом.
    private const string Message = """
        {
          "BookingId": "11111111-1111-1111-1111-111111111111",
          "EventId": "22222222-2222-2222-2222-222222222222",
          "UserId": "33333333-3333-3333-3333-333333333333",
          "SeatsCount": 2,
          "ConfirmedAt": "2026-10-18T14:00:00.1234567+00:00"
        }
        """;

    [Fact]
    public void Deserialize_RecoversAllPublicFields()
    {
        var message = JsonSerializer.Deserialize<BookingConfirmed>(Message);
        Assert.NotNull(message);
        Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), message.BookingId);
        Assert.Equal(Guid.Parse("22222222-2222-2222-2222-222222222222"), message.EventId);
        Assert.Equal(Guid.Parse("33333333-3333-3333-3333-333333333333"), message.UserId);
        Assert.Equal(2, message.SeatsCount);
        Assert.Equal(new DateTimeOffset(2026, 10, 18, 14, 0, 0, TimeSpan.Zero).AddTicks(1234567), message.ConfirmedAt);
        Assert.Equal(TimeSpan.Zero, message.ConfirmedAt.Offset);
    }

    [Fact]
    public void Serialize_ContainsOnlyContractFieldsAndRoundTrips()
    {
        var message = new BookingConfirmed(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1,
            new DateTimeOffset(2026, 10, 18, 14, 0, 0, TimeSpan.Zero));
        var json = JsonSerializer.Serialize(message);
        using var document = JsonDocument.Parse(json);
        Assert.Equal(
            new[] { "BookingId", "ConfirmedAt", "EventId", "SeatsCount", "UserId" },
            document.RootElement.EnumerateObject().Select(p => p.Name).OrderBy(name => name, StringComparer.Ordinal));
        Assert.Equal(message, JsonSerializer.Deserialize<BookingConfirmed>(json));
    }
}
