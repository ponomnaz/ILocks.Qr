using System.Security.Claims;
using System.Text.Json;
using Api.Contracts.Common;
using Api.Contracts.Qr;
using Api.Contracts.Telegram;
using Domain.Entities;
using FluentValidation;
using Infrastructure.Persistence;
using Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace Api.Endpoints;

internal static class QrEndpoints
{
    public static RouteGroupBuilder MapQrEndpoints(this IEndpointRouteBuilder app)
    {
        var qrGroup = app.MapGroup("/api/qr")
            .WithTags("QR")
            .RequireAuthorization();

        qrGroup.MapPost("", async (
            CreateQrRequest request,
            ClaimsPrincipal principal,
            AppDbContext db,
            IQrCodeService qrCodeService,
            IValidator<CreateQrRequest> validator,
            CancellationToken ct) =>
        {
            var validationResult = await validator.ValidateAsync(request, ct);
            if (!validationResult.IsValid)
            {
                return Results.ValidationProblem(validationResult.ToDictionary());
            }

            if (!EndpointUserContext.TryGetUserId(principal, out var userId))
            {
                return Results.Unauthorized();
            }

            var user = await db.Users.SingleOrDefaultAsync(x => x.Id == userId, ct);
            if (user is null)
            {
                return Results.Unauthorized();
            }

            var now = DateTimeOffset.UtcNow;
            var dataType = string.IsNullOrWhiteSpace(request.DataType)
                ? "booking_access"
                : request.DataType.Trim();

            var payloadObject = new
            {
                checkInAt = request.CheckInAt,
                checkOutAt = request.CheckOutAt,
                guestsCount = request.GuestsCount,
                doorPassword = request.DoorPassword,
                userId = user.Id,
                phoneNumber = user.PhoneNumber,
                dataType,
                createdAt = now
            };

            var payloadJson = JsonSerializer.Serialize(payloadObject);
            var qrImageBase64 = qrCodeService.GeneratePngBase64(payloadJson);

            var record = new QrCodeRecord
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                CheckInAt = request.CheckInAt,
                CheckOutAt = request.CheckOutAt,
                GuestsCount = request.GuestsCount,
                DoorPassword = request.DoorPassword.Trim(),
                PayloadJson = payloadJson,
                QrImageBase64 = qrImageBase64,
                DataType = dataType,
                CreatedAt = now
            };

            db.QrCodeRecords.Add(record);
            await db.SaveChangesAsync(ct);

            return Results.Ok(new CreateQrResponse(
                record.Id,
                record.CheckInAt,
                record.CheckOutAt,
                record.GuestsCount,
                record.DataType,
                record.CreatedAt,
                record.PayloadJson,
                record.QrImageBase64));
        })
            .WithName("CreateQr")
            .WithSummary("Generate and save QR")
            .WithDescription("Generates PNG QR from booking payload and stores record in database for current user.")
            .Produces<CreateQrResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized);

        qrGroup.MapGet("", async (
            ClaimsPrincipal principal,
            AppDbContext db,
            int? skip,
            int? take,
            CancellationToken ct) =>
        {
            if (!EndpointUserContext.TryGetUserId(principal, out var userId))
            {
                return Results.Unauthorized();
            }

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
                .Select(x => new QrCodeListItemResponse(
                    x.Id,
                    x.CheckInAt,
                    x.CheckOutAt,
                    x.GuestsCount,
                    x.DataType,
                    x.CreatedAt))
                .ToListAsync(ct);

            return Results.Ok(new QrCodeHistoryResponse(items, total, resolvedSkip, resolvedTake));
        })
            .WithName("GetQrHistory")
            .WithSummary("Get QR history")
            .WithDescription("Returns paged QR records for current user.")
            .Produces<QrCodeHistoryResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

        qrGroup.MapGet("/{id:guid}", async (
            Guid id,
            ClaimsPrincipal principal,
            AppDbContext db,
            CancellationToken ct) =>
        {
            if (!EndpointUserContext.TryGetUserId(principal, out var userId))
            {
                return Results.Unauthorized();
            }

            var record = await db.QrCodeRecords
                .AsNoTracking()
                .Where(x => x.Id == id && x.UserId == userId)
                .Select(x => new QrCodeDetailsResponse(
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
                return Results.NotFound(new ErrorResponse(
                    "qr_not_found",
                    "QR record not found."));
            }

            return Results.Ok(record);
        })
            .WithName("GetQrById")
            .WithSummary("Get QR details by id")
            .WithDescription("Returns single QR record for current user.")
            .Produces<QrCodeDetailsResponse>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized);

        qrGroup.MapPost("/{id:guid}/send-telegram", async (
            Guid id,
            ClaimsPrincipal principal,
            AppDbContext db,
            ITelegramQrSender telegramQrSender,
            CancellationToken ct) =>
        {
            if (!EndpointUserContext.TryGetUserId(principal, out var userId))
            {
                return Results.Unauthorized();
            }

            var record = await db.QrCodeRecords
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId, ct);

            if (record is null)
            {
                return Results.NotFound(new ErrorResponse(
                    "qr_not_found",
                    "QR record not found."));
            }

            var binding = await db.TelegramBindings
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.UserId == userId, ct);

            if (binding is null)
            {
                return Results.BadRequest(new ErrorResponse(
                    "telegram_not_bound",
                    "Telegram chat is not bound for this user."));
            }

            var caption = $"QR access ({record.DataType}) | {record.CheckInAt:O} - {record.CheckOutAt:O}";

            try
            {
                await telegramQrSender.SendQrCodeAsync(binding.ChatId, record.QrImageBase64, caption, ct);
            }
            catch (TelegramIntegrationException ex) when (ex.Error == TelegramIntegrationError.Configuration)
            {
                return Results.Problem(
                    title: "Telegram is not configured",
                    detail: ex.Message,
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
            catch (TelegramIntegrationException ex) when (ex.Error == TelegramIntegrationError.InvalidChat)
            {
                return Results.BadRequest(new ErrorResponse("telegram_invalid_chat", ex.Message));
            }
            catch (TelegramIntegrationException ex) when (ex.Error == TelegramIntegrationError.Forbidden)
            {
                return Results.Problem(
                    title: "Telegram access denied",
                    detail: ex.Message,
                    statusCode: StatusCodes.Status403Forbidden);
            }
            catch (TelegramIntegrationException ex) when (ex.Error == TelegramIntegrationError.Timeout)
            {
                return Results.Problem(
                    title: "Telegram timeout",
                    detail: ex.Message,
                    statusCode: StatusCodes.Status504GatewayTimeout);
            }
            catch (TelegramIntegrationException ex) when (ex.Error == TelegramIntegrationError.Network)
            {
                return Results.Problem(
                    title: "Telegram network failure",
                    detail: ex.Message,
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
            catch (TelegramIntegrationException ex) when (ex.Error == TelegramIntegrationError.InvalidPayload)
            {
                return Results.Problem(
                    title: "Stored QR payload is invalid",
                    detail: ex.Message,
                    statusCode: StatusCodes.Status500InternalServerError);
            }
            catch (TelegramIntegrationException ex)
            {
                return Results.Problem(
                    title: "Telegram API error",
                    detail: ex.Message,
                    statusCode: StatusCodes.Status502BadGateway);
            }

            return Results.Ok(new SendQrToTelegramResponse(
                record.Id,
                binding.ChatId,
                DateTimeOffset.UtcNow,
                "sent"));
        })
            .WithName("SendQrToTelegram")
            .WithSummary("Send QR to Telegram")
            .WithDescription("Sends stored QR PNG to bound Telegram chat for current user.")
            .Produces<SendQrToTelegramResponse>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status500InternalServerError)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .ProducesProblem(StatusCodes.Status504GatewayTimeout);

        return qrGroup;
    }
}
