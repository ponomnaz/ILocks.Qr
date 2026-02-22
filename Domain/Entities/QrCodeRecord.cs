namespace Domain.Entities;

public sealed class QrCodeRecord
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public DateTimeOffset CheckInAt { get; set; }
    public DateTimeOffset CheckOutAt { get; set; }
    public int GuestsCount { get; set; }
    public string DoorPassword { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = string.Empty;
    public string QrImageBase64 { get; set; } = string.Empty;
    public string DataType { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }

    public User User { get; set; } = null!;
}
