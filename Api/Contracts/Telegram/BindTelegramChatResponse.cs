namespace Api.Contracts.Telegram;

public sealed record BindTelegramChatResponse(Guid UserId, long ChatId, DateTimeOffset BoundAtUtc);
