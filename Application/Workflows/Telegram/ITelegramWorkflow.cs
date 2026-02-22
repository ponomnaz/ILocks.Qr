namespace Application.Workflows.Telegram;

public interface ITelegramWorkflow
{
    Task<BindTelegramChatWorkflowResult> BindChatAsync(Guid userId, long chatId, CancellationToken ct);
}

public enum BindTelegramChatWorkflowStatus
{
    Success,
    InvalidChat,
    UnauthorizedUser,
    ChatAlreadyBound
}

public sealed record BindTelegramChatWorkflowData(
    Guid UserId,
    long ChatId,
    DateTimeOffset BoundAtUtc);

public sealed record BindTelegramChatWorkflowResult(
    BindTelegramChatWorkflowStatus Status,
    BindTelegramChatWorkflowData? Data = null);
