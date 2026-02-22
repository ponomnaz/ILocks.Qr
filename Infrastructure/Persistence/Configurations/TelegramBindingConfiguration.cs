using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public sealed class TelegramBindingConfiguration : IEntityTypeConfiguration<TelegramBinding>
{
    public void Configure(EntityTypeBuilder<TelegramBinding> builder)
    {
        builder.ToTable("telegram_bindings");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.UserId)
            .IsRequired();

        builder.Property(x => x.ChatId)
            .IsRequired();

        builder.Property(x => x.CreatedAt)
            .IsRequired();

        builder.HasIndex(x => x.UserId)
            .IsUnique();

        builder.HasIndex(x => x.ChatId)
            .IsUnique();
    }
}
