namespace Api.Contracts.Qr;

public sealed record CreateQrRequest(
    DateTimeOffset CheckInAt,
    DateTimeOffset CheckOutAt,
    int GuestsCount,
    string DoorPassword,
    string DataType);
