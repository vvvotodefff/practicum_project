using Microsoft.EntityFrameworkCore;
using ProjectWork.Domain.Entities;

namespace ProjectWork.Infrastructure.Persistence;

/// <summary>Контекст событий и бронирований. Один экземпляр на scope.</summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Event> Events => Set<Event>();
    public DbSet<Booking> Bookings => Set<Booking>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
