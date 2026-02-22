namespace Application.Workflows.Qr;

public interface IQrWorkflow
{
    Task<CreateQrWorkflowResult> CreateAsync(Guid userId, CreateQrWorkflowCommand command, CancellationToken ct);
    Task<QrHistoryWorkflowData> GetHistoryAsync(Guid userId, int? skip, int? take, CancellationToken ct);
    Task<GetQrByIdWorkflowResult> GetByIdAsync(Guid userId, Guid qrId, CancellationToken ct);
    Task<SendQrToTelegramWorkflowResult> SendToTelegramAsync(Guid userId, Guid qrId, CancellationToken ct);
}

public sealed record CreateQrWorkflowCommand(
    DateTimeOffset CheckInAt,
    DateTimeOffset CheckOutAt,
    int GuestsCount,
    string DoorPassword,
    string DataType);

public enum CreateQrWorkflowStatus
{
    Success,
    UnauthorizedUser
}

public sealed record CreateQrWorkflowData(
    Guid Id,
    DateTimeOffset CheckInAt,
    DateTimeOffset CheckOutAt,
    int GuestsCount,
    string DataType,
    DateTimeOffset CreatedAt,
    string PayloadJson,
    string QrImageBase64);

public sealed record CreateQrWorkflowResult(
    CreateQrWorkflowStatus Status,
    CreateQrWorkflowData? Data = null);

public sealed record QrHistoryItemWorkflowData(
    Guid Id,
    DateTimeOffset CheckInAt,
    DateTimeOffset CheckOutAt,
    int GuestsCount,
    string DataType,
    DateTimeOffset CreatedAt);

public sealed record QrHistoryWorkflowData(
    IReadOnlyList<QrHistoryItemWorkflowData> Items,
    int Total,
    int Skip,
    int Take);

public enum GetQrByIdWorkflowStatus
{
    Success,
    NotFound
}

public sealed record QrDetailsWorkflowData(
    Guid Id,
    DateTimeOffset CheckInAt,
    DateTimeOffset CheckOutAt,
    int GuestsCount,
    string DoorPassword,
    string DataType,
    DateTimeOffset CreatedAt,
    string PayloadJson,
    string QrImageBase64);

public sealed record GetQrByIdWorkflowResult(
    GetQrByIdWorkflowStatus Status,
    QrDetailsWorkflowData? Data = null);

public enum SendQrToTelegramWorkflowStatus
{
    Success,
    QrNotFound,
    TelegramNotBound,
    TelegramConfiguration,
    TelegramInvalidChat,
    TelegramForbidden,
    TelegramTimeout,
    TelegramNetwork,
    TelegramInvalidPayload,
    TelegramRemoteApi
}

public sealed record SendQrToTelegramWorkflowData(
    Guid QrId,
    long ChatId,
    DateTimeOffset SentAtUtc,
    string Status);

public sealed record SendQrToTelegramWorkflowResult(
    SendQrToTelegramWorkflowStatus Status,
    SendQrToTelegramWorkflowData? Data = null,
    string? ErrorMessage = null);
