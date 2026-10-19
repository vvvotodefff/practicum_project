using ProjectWork.Users.Domain.Entities;

namespace ProjectWork.Users.Application.Abstractions.Security;

/// <summary>Выдача подписанного токена для пользователя.</summary>
public interface IJwtTokenGenerator
{
    /// <summary>Создать токен с идентификатором, логином и ролью пользователя.</summary>
    string Generate(User user);
}
