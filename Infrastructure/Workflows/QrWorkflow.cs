using System.Text.Json;
using Application.Workflows.Qr;
using Infrastructure.Persistence;
using Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Workflows;

public sealed class QrWorkflow(
    AppDbContext db,
    IQrCodeService qrCodeService,
    ITelegramQrSender telegramQrSender) : IQrWorkflow
{
    public async Task<CreateQrWorkflowResult> CreateAsync(Guid userId, CreateQrWorkflowCommand command, CancellationToken ct)
    {
        var user = await db.Users.SingleOrDefaultAsync(x => x.Id == userId, ct);
        if (user is null)
        {
            return new CreateQrWorkflowResult(CreateQrWorkflowStatus.UnauthorizedUser);
        }

        var now = DateTimeOffset.UtcNow;
        var dataType = string.IsNullOrWhiteSpace(command.DataType)
            ? "booking_access"
            : command.DataType.Trim();

        var payloadObject = new
        {
            checkInAt = command.CheckInAt,
            checkOutAt = command.CheckOutAt,
            guestsCount = command.GuestsCount,
            doorPassword = command.DoorPassword,
            userId = user.Id,
            phoneNumber = user.PhoneNumber,
            dataType,
            createdAt = now
        };

        var payloadJson = JsonSerializer.Serialize(payloadObject);
        var qrImageBase64 = qrCodeService.GeneratePngBase64(payloadJson);

        var record = new Domain.Entities.QrCodeRecord
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            CheckInAt = command.CheckInAt,
            CheckOutAt = command.CheckOutAt,
            GuestsCount = command.GuestsCount,
            DoorPassword = command.DoorPassword.Trim(),
            PayloadJson = payloadJson,
            QrImageBase64 = qrImageBase64,
            DataType = dataType,
            CreatedAt = now
        };

        db.QrCodeRecords.Add(record);
        await db.SaveChangesAsync(ct);

        return new CreateQrWorkflowResult(
            CreateQrWorkflowStatus.Success,
            new CreateQrWorkflowData(
                record.Id,
                record.CheckInAt,
                record.CheckOutAt,
                record.GuestsCount,
                record.DataType,
                record.CreatedAt,
                record.PayloadJson,
                record.QrImageBase64));
    }

    public async Task<QrHistoryWorkflowData> GetHistoryAsync(Guid userId, int? skip, int? take, CancellationToken ct)
    {
        var resolvedSkip = Math.Max(0, skip ?? 0);
        var resolvedTake = Math.Clamp(take ?? 20, 1, 100);

        var userRecordsQuery = db.QrCodeRecords
            .AsNoTracking()
            .Where(x => x.UserId == userId);

        var total = await userRecordsQuery.CountAsync(ct);

        var items = await userRecordsQuery
            .OrderByDescending(x => x.CreatedAt)
            .Skip(resolvedSkip)
            .Take(resolvedTake)
            .Select(x => new QrHistoryItemWorkflowData(
                x.Id,
                x.CheckInAt,
                x.CheckOutAt,
                x.GuestsCount,
                x.DataType,
                x.CreatedAt))
            .ToListAsync(ct);

        return new QrHistoryWorkflowData(items, total, resolvedSkip, resolvedTake);
    }

    public async Task<GetQrByIdWorkflowResult> GetByIdAsync(Guid userId, Guid qrId, CancellationToken ct)
    {
        var record = await db.QrCodeRecords
            .AsNoTracking()
            .Where(x => x.Id == qrId && x.UserId == userId)
            .Select(x => new QrDetailsWorkflowData(
                x.Id,
                x.CheckInAt,
                x.CheckOutAt,
                x.GuestsCount,
                x.DoorPassword,
                x.DataType,
                x.CreatedAt,
                x.PayloadJson,
                x.QrImageBase64))
            .SingleOrDefaultAsync(ct);

        if (record is null)
        {
            return new GetQrByIdWorkflowResult(GetQrByIdWorkflowStatus.NotFound);
        }

        return new GetQrByIdWorkflowResult(GetQrByIdWorkflowStatus.Success, record);
    }

    public async Task<SendQrToTelegramWorkflowResult> SendToTelegramAsync(Guid userId, Guid qrId, CancellationToken ct)
    {
        var record = await db.QrCodeRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == qrId && x.UserId == userId, ct);

        if (record is null)
        {
            return new SendQrToTelegramWorkflowResult(SendQrToTelegramWorkflowStatus.QrNotFound);
        }

        var binding = await db.TelegramBindings
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.UserId == userId, ct);

        if (binding is null)
        {
            return new SendQrToTelegramWorkflowResult(SendQrToTelegramWorkflowStatus.TelegramNotBound);
        }

        var caption = $"QR access ({record.DataType}) | {record.CheckInAt:O} - {record.CheckOutAt:O}";

        try
        {
            await telegramQrSender.SendQrCodeAsync(binding.ChatId, record.QrImageBase64, caption, ct);
        }
        catch (TelegramIntegrationException ex) when (ex.Error == TelegramIntegrationError.Configuration)
        {
            return new SendQrToTelegramWorkflowResult(SendQrToTelegramWorkflowStatus.TelegramConfiguration, ErrorMessage: ex.Message);
        }
        catch (TelegramIntegrationException ex) when (ex.Error == TelegramIntegrationError.InvalidChat)
        {
            return new SendQrToTelegramWorkflowResult(SendQrToTelegramWorkflowStatus.TelegramInvalidChat, ErrorMessage: ex.Message);
        }
        catch (TelegramIntegrationException ex) when (ex.Error == TelegramIntegrationError.Forbidden)
        {
            return new SendQrToTelegramWorkflowResult(SendQrToTelegramWorkflowStatus.TelegramForbidden, ErrorMessage: ex.Message);
        }
        catch (TelegramIntegrationException ex) when (ex.Error == TelegramIntegrationError.Timeout)
        {
            return new SendQrToTelegramWorkflowResult(SendQrToTelegramWorkflowStatus.TelegramTimeout, ErrorMessage: ex.Message);
        }
        catch (TelegramIntegrationException ex) when (ex.Error == TelegramIntegrationError.Network)
        {
            return new SendQrToTelegramWorkflowResult(SendQrToTelegramWorkflowStatus.TelegramNetwork, ErrorMessage: ex.Message);
        }
        catch (TelegramIntegrationException ex) when (ex.Error == TelegramIntegrationError.InvalidPayload)
        {
            return new SendQrToTelegramWorkflowResult(SendQrToTelegramWorkflowStatus.TelegramInvalidPayload, ErrorMessage: ex.Message);
        }
        catch (TelegramIntegrationException ex)
        {
            return new SendQrToTelegramWorkflowResult(SendQrToTelegramWorkflowStatus.TelegramRemoteApi, ErrorMessage: ex.Message);
        }

        return new SendQrToTelegramWorkflowResult(
            SendQrToTelegramWorkflowStatus.Success,
            new SendQrToTelegramWorkflowData(
                record.Id,
                binding.ChatId,
                DateTimeOffset.UtcNow,
                "sent"));
    }
}
