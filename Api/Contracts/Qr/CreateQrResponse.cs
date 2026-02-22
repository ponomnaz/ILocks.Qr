namespace Api.Contracts.Qr;

public sealed record CreateQrResponse(
    Guid Id,
    DateTimeOffset CheckInAt,
    DateTimeOffset CheckOutAt,
    int GuestsCount,
    string DataType,
    DateTimeOffset CreatedAt,
    string PayloadJson,
    string QrImageBase64);
