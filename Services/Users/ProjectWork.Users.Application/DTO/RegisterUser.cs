using System.ComponentModel.DataAnnotations;
using ProjectWork.Users.Domain.Entities;

namespace ProjectWork.Users.Application.DTO;

/// <summary>Данные регистрации пользователя. Роль Admin допустима в учебном API.</summary>
public class RegisterUser
{
    /// <summary>Логин пользователя.</summary>
    [Required]
    [StringLength(200)]
    public required string Login { get; set; }

    /// <summary>Пароль пользователя.</summary>
    [Required]
    public required string Password { get; set; }

    /// <summary>Роль пользователя, по умолчанию User.</summary>
    [EnumDataType(typeof(UserRole))]
    public UserRole Role { get; set; } = UserRole.User;
}
