namespace ProjectWork.Contracts;

/// <summary>Публичное сообщение о подтверждении брони сервисом Bookings.</summary>
/// <param name="BookingId">Идентификатор подтверждённой брони.</param>
/// <param name="EventId">Идентификатор события, на котором заняты места.</param>
/// <param name="UserId">Идентификатор владельца брони.</param>
/// <param name="SeatsCount">Количество занятых мест; для текущей брони — 1.</param>
/// <param name="ConfirmedAt">Момент подтверждения; издатель передаёт время в UTC.</param>
public sealed record BookingConfirmed(
    Guid BookingId,
    Guid EventId,
    Guid UserId,
    int SeatsCount,
    DateTimeOffset ConfirmedAt);
