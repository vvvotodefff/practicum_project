namespace ProjectWork.Domain.Exceptions;

/// <summary>Запрошенное событие или бронирование не найдено.</summary>
public class NotFoundException : Exception
{
    /// <summary>Создаёт исключение с описанием отсутствующего ресурса.</summary>
    public NotFoundException(string message) : base(message)
    {
    }
}

