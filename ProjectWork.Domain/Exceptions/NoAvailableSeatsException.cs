namespace ProjectWork.Domain.Exceptions;

/// <summary>
/// Бросается, когда на событии не осталось свободных мест
/// </summary>
public class NoAvailableSeatsException : Exception
{
    /// <summary>
    /// Создаёт исключение с описанием причины
    /// </summary>
    public NoAvailableSeatsException(string message) : base(message)
    {
    }
}
