using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProjectWork.Domain.Entities;

namespace ProjectWork.Infrastructure.Persistence.Configurations;

public sealed class EventConfiguration : IEntityTypeConfiguration<Event>
{
    public void Configure(EntityTypeBuilder<Event> builder)
    {
        builder.ToTable("events", table =>
        {
            table.HasCheckConstraint("ck_events_dates", "end_at > start_at");
            table.HasCheckConstraint("ck_events_seats",
                "total_seats > 0 AND available_seats >= 0 AND available_seats <= total_seats");
        });
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(e => e.Title).HasColumnName("title").IsRequired().HasMaxLength(200);
        builder.Property(e => e.Description).HasColumnName("description").HasMaxLength(2000);
        builder.Property(e => e.StartAt).HasColumnName("start_at").IsRequired();
        builder.Property(e => e.EndAt).HasColumnName("end_at").IsRequired();
        builder.Property(e => e.TotalSeats).HasColumnName("total_seats").IsRequired();
        builder.Property(e => e.AvailableSeats).HasColumnName("available_seats").IsRequired();
        builder.HasMany(e => e.Bookings).WithOne(b => b.Event)
            .HasForeignKey(b => b.EventId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(e => e.StartAt);
    }
}
