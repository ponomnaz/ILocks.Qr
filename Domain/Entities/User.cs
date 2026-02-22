namespace Domain.Entities;

public sealed class User
{
    public Guid Id { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }

    public ICollection<QrCodeRecord> QrCodeRecords { get; set; } = new List<QrCodeRecord>();
    public TelegramBinding? TelegramBinding { get; set; }
}
