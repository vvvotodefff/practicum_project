using System.ComponentModel.DataAnnotations;

namespace ProjectWork.Application.DTO;

/// <summary>Учётные данные для получения токена.</summary>
public class LoginUser
{
    /// <summary>Логин пользователя.</summary>
    [Required]
    public required string Login { get; set; }

    /// <summary>Пароль пользователя.</summary>
    [Required]
    public required string Password { get; set; }
}
