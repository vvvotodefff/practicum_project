namespace ProjectWork.Domain.Exceptions;

/// <summary>Данные нарушают правила создания или изменения доменной сущности.</summary>
public class DomainValidationException : Exception
{
    /// <summary>Создаёт исключение с описанием нарушенных правил.</summary>
    public DomainValidationException(string message) : base(message)
    {
    }
}
