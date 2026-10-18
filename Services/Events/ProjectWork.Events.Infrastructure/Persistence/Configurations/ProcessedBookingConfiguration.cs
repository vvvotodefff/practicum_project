using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ProjectWork.Events.Infrastructure.Persistence.Configurations;

public sealed class ProcessedBookingConfiguration : IEntityTypeConfiguration<ProcessedBooking>
{
    public void Configure(EntityTypeBuilder<ProcessedBooking> builder)
    {
        builder.ToTable("processed_bookings");
        // Первичный ключ не даёт дважды применить одну бронь даже при гонке экземпляров.
        builder.HasKey(p => p.BookingId);
        builder.Property(p => p.BookingId).HasColumnName("booking_id").ValueGeneratedNever();
        builder.Property(p => p.ProcessedAt).HasColumnName("processed_at").IsRequired();
    }
}
