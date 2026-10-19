using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProjectWork.Bookings.Domain.Entities;

namespace ProjectWork.Bookings.Infrastructure.Persistence.Configurations;

public sealed class BookingConfiguration : IEntityTypeConfiguration<Booking>
{
    public void Configure(EntityTypeBuilder<Booking> builder)
    {
        builder.ToTable("bookings");
        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(b => b.EventId).HasColumnName("event_id").IsRequired();
        builder.Property(b => b.UserId).HasColumnName("user_id").IsRequired();
        builder.HasIndex(b => new { b.UserId, b.Status });
        builder.Property(b => b.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(b => b.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(b => b.ProcessedAt).HasColumnName("processed_at").IsRequired(false);
        builder.HasIndex(b => b.EventId);
        builder.HasIndex(b => b.Status);
    }
}
