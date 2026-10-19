using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace ProjectWork.IntegrationTests;

public abstract class DatabaseTest<TContext>(PostgresFixture fixture) : IAsyncLifetime where TContext : DbContext
{
    private AsyncServiceScope _scope;
    protected IServiceProvider Services => _scope.ServiceProvider;
    protected TContext Context => Services.GetRequiredService<TContext>();
    protected AsyncServiceScope NewScope() => fixture.CreateScope();
    public async Task InitializeAsync()
    {
        await fixture.ResetAsync<TContext>();
        _scope = fixture.CreateScope();
    }
    public async Task DisposeAsync() => await _scope.DisposeAsync();
    protected static DateTime At(int day, int hour = 10) => new(2026, 11, day, hour, 0, 0, DateTimeKind.Utc);
    protected static async Task AssertPostgresErrorAsync(Func<Task> action, string sqlState, string? constraintName = null)
    {
        var exception = await Assert.ThrowsAsync<DbUpdateException>(action);
        var postgres = Assert.IsType<PostgresException>(exception.InnerException);
        Assert.Equal(sqlState, postgres.SqlState);
        if (constraintName is not null) Assert.Equal(constraintName, postgres.ConstraintName);
    }
}
