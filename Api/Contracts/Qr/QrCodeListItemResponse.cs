namespace Api.Contracts.Qr;

public sealed record QrCodeListItemResponse(
    Guid Id,
    DateTimeOffset CheckInAt,
    DateTimeOffset CheckOutAt,
    int GuestsCount,
    string DataType,
    DateTimeOffset CreatedAt);
