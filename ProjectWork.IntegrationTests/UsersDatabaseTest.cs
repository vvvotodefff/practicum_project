using Microsoft.Extensions.DependencyInjection;
using ProjectWork.Users.Application.Abstractions;
using ProjectWork.Users.Application.Abstractions.Repositories;
using ProjectWork.Users.Domain.Entities;
using ProjectWork.Users.Infrastructure.Persistence;

namespace ProjectWork.IntegrationTests;

public abstract class UsersDatabaseTest(PostgresFixture fixture) : DatabaseTest<UsersDbContext>(fixture)
{
    protected IUnitOfWork UnitOfWork => Services.GetRequiredService<IUnitOfWork>();

}
