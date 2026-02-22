using QRCoder;

namespace Infrastructure.Services;

public interface IQrCodeService
{
    string GeneratePngBase64(string payload);
}

public sealed class QrCodeService : IQrCodeService
{
    public string GeneratePngBase64(string payload)
    {
        using var generator = new QRCodeGenerator();
        using var qrData = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.Q);
        var qrCode = new PngByteQRCode(qrData);
        var pngBytes = qrCode.GetGraphic(20);
        return Convert.ToBase64String(pngBytes);
    }
}
