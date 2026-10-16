using ProjectWork.Domain.Entities;

namespace ProjectWork.Application.Abstractions.Repositories;

/// <summary>Чтение и подготовка сохранения пользователей.</summary>
public interface IUserRepository
{
    /// <summary>Найти пользователя по идентификатору.</summary>
    Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    /// <summary>Найти пользователя по логину.</summary>
    Task<User?> GetByLoginAsync(string login, CancellationToken cancellationToken = default);
    /// <summary>Подготовить добавление пользователя.</summary>
    Task AddAsync(User user, CancellationToken cancellationToken = default);
}
