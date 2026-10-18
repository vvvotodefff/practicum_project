using Microsoft.EntityFrameworkCore;
using ProjectWork.Users.Application.Abstractions.Repositories;
using ProjectWork.Users.Domain.Entities;
using ProjectWork.Users.Infrastructure.Persistence;

namespace ProjectWork.Users.Infrastructure.Repositories;

public sealed class UserRepository(UsersDbContext context) : IUserRepository
{
    public Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        context.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == id, cancellationToken);

    public Task<User?> GetByLoginAsync(string login, CancellationToken cancellationToken = default) =>
        context.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Login == login, cancellationToken);

    public Task AddAsync(User user, CancellationToken cancellationToken = default)
    {
        context.Users.Add(user);
        return Task.CompletedTask;
    }
}
