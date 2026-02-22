namespace Api.Contracts.Qr;

public sealed record QrCodeHistoryResponse(
    IReadOnlyList<QrCodeListItemResponse> Items,
    int Total,
    int Skip,
    int Take);
