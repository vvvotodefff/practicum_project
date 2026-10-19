using ProjectWork.Users.Application.Abstractions;
using ProjectWork.Users.Application.Abstractions.Repositories;
using ProjectWork.Users.Application.Abstractions.Security;
using ProjectWork.Users.Application.DTO;
using ProjectWork.Users.Domain.Entities;
using ProjectWork.Users.Domain.Exceptions;

namespace ProjectWork.Users.Application.Services;

/// <summary>Регистрация и вход без зависимости от HTTP и алгоритма хеширования.</summary>
public sealed class UserService(IUserRepository users, IPasswordHasher hasher,
    IJwtTokenGenerator tokens, IUnitOfWork unitOfWork) : IUserService
{
    /// <inheritdoc />
    public async Task RegisterAsync(RegisterUser request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Password))
            throw new DomainValidationException("Пароль обязателен.");
        var user = User.Create(request.Login, hasher.Hash(request.Password), request.Role);
        if (await users.GetByLoginAsync(user.Login, cancellationToken) is not null)
            throw new DomainValidationException("Логин уже занят.");
        await users.AddAsync(user, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<TokenInfo> LoginAsync(LoginUser request, CancellationToken cancellationToken = default)
    {
        var user = string.IsNullOrWhiteSpace(request.Login)
            ? null : await users.GetByLoginAsync(request.Login.Trim(), cancellationToken);
        if (user is null || !hasher.Verify(request.Password, user.PasswordHash))
            throw new DomainValidationException("Неверный логин или пароль.");
        return new TokenInfo(tokens.Generate(user));
    }
}
