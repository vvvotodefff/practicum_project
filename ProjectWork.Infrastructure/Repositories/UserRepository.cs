using Microsoft.EntityFrameworkCore;
using ProjectWork.Application.Abstractions.Repositories;
using ProjectWork.Domain.Entities;
using ProjectWork.Infrastructure.Persistence;

namespace ProjectWork.Infrastructure.Repositories;

public sealed class UserRepository(AppDbContext context) : IUserRepository
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
