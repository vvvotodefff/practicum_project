namespace ProjectWork.Domain.Exceptions;

/// <summary>У пользователя нет прав на операцию.</summary>
public class ForbiddenOperationException(string message) : Exception(message);
