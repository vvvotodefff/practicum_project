using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProjectWork.Users.Domain.Entities;

namespace ProjectWork.Users.Infrastructure.Persistence.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");
        builder.HasKey(u => u.Id);
        builder.Property(u => u.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(u => u.Login).HasColumnName("login").HasMaxLength(200).IsRequired();
        builder.HasIndex(u => u.Login).IsUnique().HasDatabaseName("IX_users_login");
        builder.Property(u => u.PasswordHash).HasColumnName("password_hash").HasMaxLength(64).IsRequired();
        builder.Property(u => u.Role).HasColumnName("role").HasConversion<string>().HasMaxLength(20).IsRequired();
    }
}
