namespace ProjectWork.Bookings.Domain.Exceptions;

/// <summary>Превышен лимит активных бронирований пользователя.</summary>
public class ActiveBookingLimitExceededException(string message) : Exception(message);
