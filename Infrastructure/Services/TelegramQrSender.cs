using System.Net.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;

namespace Infrastructure.Services;

public interface ITelegramQrSender
{
    Task SendQrCodeAsync(long chatId, string qrImageBase64, string caption, CancellationToken ct);
}

public sealed class TelegramQrSender(
    IConfiguration configuration,
    ILogger<TelegramQrSender> logger) : ITelegramQrSender
{
    private readonly string? _botToken = configuration["Telegram:BotToken"];

    public async Task SendQrCodeAsync(long chatId, string qrImageBase64, string caption, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_botToken))
        {
            throw new TelegramIntegrationException(
                TelegramIntegrationError.Configuration,
                "Telegram bot token is not configured.");
        }

        byte[] imageBytes;
        try
        {
            imageBytes = Convert.FromBase64String(qrImageBase64);
        }
        catch (FormatException)
        {
            throw new TelegramIntegrationException(
                TelegramIntegrationError.InvalidPayload,
                "QR payload is not valid Base64.");
        }

        var botClient = new TelegramBotClient(_botToken);

        try
        {
            await using var stream = new MemoryStream(imageBytes);
            var inputFile = InputFile.FromStream(stream, "qr-code.png");

            await botClient.SendPhoto(
                chatId: chatId,
                photo: inputFile,
                caption: caption,
                cancellationToken: ct);
        }
        catch (ApiRequestException ex) when (ex.ErrorCode == 400)
        {
            logger.LogWarning(ex, "Telegram rejected request with 400 for chat {ChatId}", chatId);
            throw new TelegramIntegrationException(
                TelegramIntegrationError.InvalidChat,
                "Invalid Telegram chat or bot has no access to it.",
                ex);
        }
        catch (ApiRequestException ex) when (ex.ErrorCode == 403)
        {
            logger.LogWarning(ex, "Telegram returned 403 for chat {ChatId}", chatId);
            throw new TelegramIntegrationException(
                TelegramIntegrationError.Forbidden,
                "Bot is blocked or has no permission to send messages.",
                ex);
        }
        catch (ApiRequestException ex)
        {
            logger.LogError(ex, "Telegram API error for chat {ChatId}", chatId);
            throw new TelegramIntegrationException(
                TelegramIntegrationError.RemoteApi,
                "Telegram API error occurred.",
                ex);
        }
        catch (TaskCanceledException ex)
        {
            logger.LogWarning(ex, "Telegram send timed out for chat {ChatId}", chatId);
            throw new TelegramIntegrationException(
                TelegramIntegrationError.Timeout,
                "Telegram request timed out.",
                ex);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Network error while sending QR to Telegram chat {ChatId}", chatId);
            throw new TelegramIntegrationException(
                TelegramIntegrationError.Network,
                "Network error while contacting Telegram.",
                ex);
        }
    }
}

public enum TelegramIntegrationError
{
    Configuration,
    InvalidPayload,
    InvalidChat,
    Forbidden,
    Timeout,
    Network,
    RemoteApi
}

public sealed class TelegramIntegrationException(
    TelegramIntegrationError error,
    string message,
    Exception? innerException = null) : Exception(message, innerException)
{
    public TelegramIntegrationError Error { get; } = error;
}
