using Microsoft.EntityFrameworkCore;
using ProjectWork.Events.Domain.Entities;

namespace ProjectWork.Events.Infrastructure.Persistence;

public sealed class EventsDbContext(DbContextOptions<EventsDbContext> options) : DbContext(options)
{
    public DbSet<Event> Events => Set<Event>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(EventsDbContext).Assembly);
}
