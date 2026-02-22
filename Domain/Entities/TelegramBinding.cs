namespace Domain.Entities;

public sealed class TelegramBinding
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public long ChatId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public User User { get; set; } = null!;
}
