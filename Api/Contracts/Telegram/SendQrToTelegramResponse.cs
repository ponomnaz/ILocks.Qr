namespace Api.Contracts.Telegram;

public sealed record SendQrToTelegramResponse(Guid QrId, long ChatId, DateTimeOffset SentAtUtc, string Status);
