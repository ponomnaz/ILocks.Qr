namespace Domain.Entities;

public sealed class OtpCode
{
    public Guid Id { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
    public string CodeHash { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
    public bool IsUsed { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
