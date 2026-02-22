using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.PhoneNumber)
            .IsRequired()
            .HasMaxLength(32);

        builder.Property(x => x.CreatedAt)
            .IsRequired();

        builder.HasIndex(x => x.PhoneNumber)
            .IsUnique();

        builder.HasMany(x => x.QrCodeRecords)
            .WithOne(x => x.User)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.TelegramBinding)
            .WithOne(x => x.User)
            .HasForeignKey<TelegramBinding>(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
