using FluentAssertions;
using Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace UnitTests.Infrastructure.Services;

public sealed class TelegramQrSenderTests
{
    [Fact]
    public async Task SendQrCodeAsync_EmptyBotToken_ThrowsConfigurationError()
    {
        var sut = CreateSut(new Dictionary<string, string?>());

        Func<Task> act = () => sut.SendQrCodeAsync(1, "AA==", "caption", CancellationToken.None);

        var exception = await act.Should().ThrowAsync<TelegramIntegrationException>();
        exception.Which.Error.Should().Be(TelegramIntegrationError.Configuration);
    }

    [Fact]
    public async Task SendQrCodeAsync_InvalidBase64_ThrowsInvalidPayloadError()
    {
        var sut = CreateSut(new Dictionary<string, string?>
        {
            ["Telegram:BotToken"] = "test-token"
        });

        Func<Task> act = () => sut.SendQrCodeAsync(1, "not-base64", "caption", CancellationToken.None);

        var exception = await act.Should().ThrowAsync<TelegramIntegrationException>();
        exception.Which.Error.Should().Be(TelegramIntegrationError.InvalidPayload);
    }

    private static TelegramQrSender CreateSut(Dictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

        return new TelegramQrSender(configuration, NullLogger<TelegramQrSender>.Instance);
    }
}
