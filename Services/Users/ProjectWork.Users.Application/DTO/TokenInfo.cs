namespace ProjectWork.Users.Application.DTO;

/// <summary>Результат успешного входа.</summary>
/// <param name="Token">Подписанный JWT.</param>
public record TokenInfo(string Token);
