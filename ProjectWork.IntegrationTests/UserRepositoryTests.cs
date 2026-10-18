using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using ProjectWork.Users.Application.Abstractions.Repositories;
using ProjectWork.Users.Domain.Entities;
using ProjectWork.Users.Domain.Exceptions;
using ProjectWork.Users.Infrastructure.Persistence;

namespace ProjectWork.IntegrationTests;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class UserRepositoryTests(PostgresFixture fixture) : UsersDatabaseTest(fixture)
{
    private IUserRepository Users => new ProjectWork.Users.Infrastructure.Repositories.UserRepository(Context);

    [Fact]
    public async Task AddAndRead_PersistsFieldsAndReturnsUntrackedUser()
    {
        var user = User.Create("admin", new string('B', 64), UserRole.Admin);
        await Users.AddAsync(user);
        await UnitOfWork.SaveChangesAsync();
        Context.ChangeTracker.Clear();
        var saved = await Users.GetByLoginAsync("admin");
        Assert.NotNull(saved);
        Assert.Equal(user.Id, saved.Id);
        Assert.Equal(user.PasswordHash, saved.PasswordHash);
        Assert.Equal(UserRole.Admin, saved.Role);
        Assert.Equal(user.Id, (await Users.GetByIdAsync(user.Id))!.Id);
        Assert.Null(await Users.GetByLoginAsync("missing"));
        Assert.Null(await Users.GetByIdAsync(Guid.NewGuid()));
        Assert.Empty(Context.ChangeTracker.Entries());
    }

    [Fact]
    public async Task UniqueLogin_IsEnforcedAcrossScopes()
    {
        await Users.AddAsync(User.Create("duplicate", new string('A', 64)));
        await UnitOfWork.SaveChangesAsync();
        await using var scope = NewScope();
        var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        await users.AddAsync(User.Create("duplicate", new string('B', 64)));
        var work = scope.ServiceProvider.GetRequiredService<ProjectWork.Users.Application.Abstractions.IUnitOfWork>();
        await Assert.ThrowsAsync<DomainValidationException>(() => work.SaveChangesAsync());
        Assert.Equal(1, await Context.Users.CountAsync(u => u.Login == "duplicate"));
    }




}
