using System;
using ProjectWork.Users.Domain.Exceptions;

namespace ProjectWork.Users.Domain.Entities;

/// <summary>Пользователь с логином, хешем пароля и ролью.</summary>
public class User
{
    private User() { }

    public Guid Id { get; private set; }

    public string Login { get; private set; } = null!;

    public string PasswordHash { get; private set; } = null!;

    public UserRole Role { get; private set; }

    /// <summary>Создать пользователя с уже вычисленным хешем пароля.</summary>
    public static User Create(string login, string passwordHash, UserRole role = UserRole.User)
    {
        if (string.IsNullOrWhiteSpace(login))
            throw new DomainValidationException("Логин обязателен.");

        if (login.Trim().Length > 200)
            throw new DomainValidationException("Логин не должен превышать 200 символов.");

        if (string.IsNullOrWhiteSpace(passwordHash))
            throw new DomainValidationException("Хеш пароля обязателен.");

        if (!Enum.IsDefined(typeof(UserRole), role))
            throw new DomainValidationException("Указана неизвестная роль пользователя.");

        return new User
        {
            Id = Guid.NewGuid(),
            Login = login.Trim(),
            PasswordHash = passwordHash,
            Role = role
        };
    }
}
