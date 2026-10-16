using ProjectWork.Application.DTO;

namespace ProjectWork.Application.Services;

/// <summary>Регистрация и проверка учётных данных.</summary>
public interface IUserService
{
    /// <summary>Зарегистрировать пользователя.</summary>
    Task RegisterAsync(RegisterUser request, CancellationToken cancellationToken = default);
    /// <summary>Проверить пароль и выдать токен.</summary>
    Task<TokenInfo> LoginAsync(LoginUser request, CancellationToken cancellationToken = default);
}
