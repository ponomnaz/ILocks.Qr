namespace Api.Contracts.Qr;

public sealed record QrCodeDetailsResponse(
    Guid Id,
    DateTimeOffset CheckInAt,
    DateTimeOffset CheckOutAt,
    int GuestsCount,
    string DoorPassword,
    string DataType,
    DateTimeOffset CreatedAt,
    string PayloadJson,
    string QrImageBase64);
