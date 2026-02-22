using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public sealed class QrCodeRecordConfiguration : IEntityTypeConfiguration<QrCodeRecord>
{
    public void Configure(EntityTypeBuilder<QrCodeRecord> builder)
    {
        builder.ToTable("qr_code_records", table =>
        {
            table.HasCheckConstraint("CK_qr_code_records_guests_count_positive", "\"GuestsCount\" > 0");
            table.HasCheckConstraint("CK_qr_code_records_checkout_after_checkin", "\"CheckOutAt\" > \"CheckInAt\"");
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.UserId)
            .IsRequired();

        builder.Property(x => x.CheckInAt)
            .IsRequired();

        builder.Property(x => x.CheckOutAt)
            .IsRequired();

        builder.Property(x => x.GuestsCount)
            .IsRequired();

        builder.Property(x => x.DoorPassword)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(x => x.PayloadJson)
            .IsRequired();

        builder.Property(x => x.QrImageBase64)
            .IsRequired();

        builder.Property(x => x.DataType)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(x => x.CreatedAt)
            .IsRequired();

        builder.HasIndex(x => x.UserId);
        builder.HasIndex(x => x.CreatedAt);

    }
}
