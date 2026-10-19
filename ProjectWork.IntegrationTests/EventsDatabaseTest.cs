using Microsoft.Extensions.DependencyInjection;
using ProjectWork.Events.Application.Abstractions;
using ProjectWork.Events.Application.Abstractions.Repositories;
using ProjectWork.Events.Domain.Entities;
using ProjectWork.Events.Infrastructure.Persistence;

namespace ProjectWork.IntegrationTests;

public abstract class EventsDatabaseTest(PostgresFixture fixture) : DatabaseTest<EventsDbContext>(fixture)
{
    protected IUnitOfWork UnitOfWork => Services.GetRequiredService<IUnitOfWork>();
    protected IEventRepository Events => Services.GetRequiredService<IEventRepository>();

    protected static Event NewEvent(string title = "Test event", int day = 1, int seats = 5) =>
        Event.Create(title, "Integration test", At(day), At(day, 12), seats);
    protected async Task SeedEventsAsync(params Event[] events)
    {
        Context.Events.AddRange(events);
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();
    }
}
