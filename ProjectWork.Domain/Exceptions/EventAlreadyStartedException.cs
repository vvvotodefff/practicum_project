namespace ProjectWork.Domain.Exceptions;

/// <summary>Попытка забронировать уже начавшееся событие.</summary>
public class EventAlreadyStartedException(string message) : Exception(message);
