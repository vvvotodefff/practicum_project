using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectWork.Users.Application.DTO;
using ProjectWork.Users.Application.Services;
using ProjectWork.Users.Domain.Entities;
using ProjectWork.Users.Domain.Exceptions;
using ProjectWork.Users.Infrastructure.Persistence;
using ProjectWork.Users.Infrastructure.Security;

namespace ProjectWork.Tests;

public class UserServiceTests
{
    [Fact]
    public async Task RegisterLogin_UsesHashAndPreservesRole()
    {
        using var db = new TestDatabase();
        using var scope = db.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<IUserService>();
        await users.RegisterAsync(new RegisterUser { Login = " admin ", Password = "secret-password", Role = UserRole.Admin });
        var saved = await scope.ServiceProvider.GetRequiredService<UsersDbContext>().Users.SingleAsync();
        Assert.Equal("admin", saved.Login);
        Assert.NotEqual("secret-password", saved.PasswordHash);
        Assert.True(new Sha256PasswordHasher().Verify("secret-password", saved.PasswordHash));
        Assert.Equal(UserRole.Admin, saved.Role);
        Assert.Equal(3, (await users.LoginAsync(new LoginUser { Login = "admin", Password = "secret-password" })).Token.Split('.').Length);
        await Assert.ThrowsAsync<DomainValidationException>(() =>
            users.RegisterAsync(new RegisterUser { Login = "admin", Password = "another" }));
        var unknown = await Assert.ThrowsAsync<DomainValidationException>(() =>
            users.LoginAsync(new LoginUser { Login = "missing", Password = "wrong" }));
        var wrong = await Assert.ThrowsAsync<DomainValidationException>(() =>
            users.LoginAsync(new LoginUser { Login = "admin", Password = "wrong" }));
        Assert.Equal(unknown.Message, wrong.Message);
    }
}
