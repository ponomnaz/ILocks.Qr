using FluentAssertions;
using Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace UnitTests.Infrastructure.Services;

public sealed class TelegramQrSenderTests
{
    [Fact]
    public async Task SendQrCodeAsync_MissingBotToken_ThrowsConfigurationException()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();
        var sender = new TelegramQrSender(configuration, NullLogger<TelegramQrSender>.Instance);

        Func<Task> act = () => sender.SendQrCodeAsync(12345, "dGVzdA==", "caption", CancellationToken.None);

        var exception = await act.Should().ThrowAsync<TelegramIntegrationException>();
        exception.Which.Error.Should().Be(TelegramIntegrationError.Configuration);
    }

    [Fact]
    public async Task SendQrCodeAsync_InvalidBase64_ThrowsInvalidPayloadException()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Telegram:BotToken"] = "test-token"
            })
            .Build();
        var sender = new TelegramQrSender(configuration, NullLogger<TelegramQrSender>.Instance);

        Func<Task> act = () => sender.SendQrCodeAsync(12345, "not-valid-base64", "caption", CancellationToken.None);

        var exception = await act.Should().ThrowAsync<TelegramIntegrationException>();
        exception.Which.Error.Should().Be(TelegramIntegrationError.InvalidPayload);
    }
}
