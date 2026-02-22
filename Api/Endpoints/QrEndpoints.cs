using System.Security.Claims;
using Api.Contracts.Common;
using Api.Contracts.Qr;
using Api.Contracts.Telegram;
using Application.Workflows.Qr;
using FluentValidation;

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
            IQrWorkflow qrWorkflow,
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

            var result = await qrWorkflow.CreateAsync(
                userId,
                new CreateQrWorkflowCommand(
                    request.CheckInAt,
                    request.CheckOutAt,
                    request.GuestsCount,
                    request.DoorPassword,
                    request.DataType),
                ct);

            return result.Status switch
            {
                CreateQrWorkflowStatus.UnauthorizedUser => Results.Unauthorized(),
                CreateQrWorkflowStatus.Success when result.Data is not null => Results.Ok(new CreateQrResponse(
                    result.Data.Id,
                    result.Data.CheckInAt,
                    result.Data.CheckOutAt,
                    result.Data.GuestsCount,
                    result.Data.DataType,
                    result.Data.CreatedAt,
                    result.Data.PayloadJson,
                    result.Data.QrImageBase64)),
                _ => Results.Problem(statusCode: StatusCodes.Status500InternalServerError)
            };
        })
            .WithName("CreateQr")
            .WithSummary("Generate and save QR")
            .WithDescription("Generates PNG QR from booking payload and stores record in database for current user.")
            .Produces<CreateQrResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized);

        qrGroup.MapGet("", async (
            ClaimsPrincipal principal,
            IQrWorkflow qrWorkflow,
            int? skip,
            int? take,
            CancellationToken ct) =>
        {
            if (!EndpointUserContext.TryGetUserId(principal, out var userId))
            {
                return Results.Unauthorized();
            }

            var history = await qrWorkflow.GetHistoryAsync(userId, skip, take, ct);

            var items = history.Items
                .Select(x => new QrCodeListItemResponse(
                    x.Id,
                    x.CheckInAt,
                    x.CheckOutAt,
                    x.GuestsCount,
                    x.DataType,
                    x.CreatedAt))
                .ToList();

            return Results.Ok(new QrCodeHistoryResponse(items, history.Total, history.Skip, history.Take));
        })
            .WithName("GetQrHistory")
            .WithSummary("Get QR history")
            .WithDescription("Returns paged QR records for current user.")
            .Produces<QrCodeHistoryResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

        qrGroup.MapGet("/{id:guid}", async (
            Guid id,
            ClaimsPrincipal principal,
            IQrWorkflow qrWorkflow,
            CancellationToken ct) =>
        {
            if (!EndpointUserContext.TryGetUserId(principal, out var userId))
            {
                return Results.Unauthorized();
            }

            var result = await qrWorkflow.GetByIdAsync(userId, id, ct);

            return result.Status switch
            {
                GetQrByIdWorkflowStatus.NotFound => Results.NotFound(new ErrorResponse(
                    "qr_not_found",
                    "QR record not found.")),
                GetQrByIdWorkflowStatus.Success when result.Data is not null => Results.Ok(new QrCodeDetailsResponse(
                    result.Data.Id,
                    result.Data.CheckInAt,
                    result.Data.CheckOutAt,
                    result.Data.GuestsCount,
                    result.Data.DoorPassword,
                    result.Data.DataType,
                    result.Data.CreatedAt,
                    result.Data.PayloadJson,
                    result.Data.QrImageBase64)),
                _ => Results.Problem(statusCode: StatusCodes.Status500InternalServerError)
            };
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
            IQrWorkflow qrWorkflow,
            CancellationToken ct) =>
        {
            if (!EndpointUserContext.TryGetUserId(principal, out var userId))
            {
                return Results.Unauthorized();
            }

            var result = await qrWorkflow.SendToTelegramAsync(userId, id, ct);

            return result.Status switch
            {
                SendQrToTelegramWorkflowStatus.QrNotFound => Results.NotFound(new ErrorResponse(
                    "qr_not_found",
                    "QR record not found.")),
                SendQrToTelegramWorkflowStatus.TelegramNotBound => Results.BadRequest(new ErrorResponse(
                    "telegram_not_bound",
                    "Telegram chat is not bound for this user.")),
                SendQrToTelegramWorkflowStatus.TelegramConfiguration => Results.Problem(
                    title: "Telegram is not configured",
                    detail: result.ErrorMessage,
                    statusCode: StatusCodes.Status503ServiceUnavailable),
                SendQrToTelegramWorkflowStatus.TelegramInvalidChat => Results.BadRequest(new ErrorResponse(
                    "telegram_invalid_chat",
                    result.ErrorMessage ?? "Invalid Telegram chat or bot has no access to it.")),
                SendQrToTelegramWorkflowStatus.TelegramForbidden => Results.Problem(
                    title: "Telegram access denied",
                    detail: result.ErrorMessage,
                    statusCode: StatusCodes.Status403Forbidden),
                SendQrToTelegramWorkflowStatus.TelegramTimeout => Results.Problem(
                    title: "Telegram timeout",
                    detail: result.ErrorMessage,
                    statusCode: StatusCodes.Status504GatewayTimeout),
                SendQrToTelegramWorkflowStatus.TelegramNetwork => Results.Problem(
                    title: "Telegram network failure",
                    detail: result.ErrorMessage,
                    statusCode: StatusCodes.Status503ServiceUnavailable),
                SendQrToTelegramWorkflowStatus.TelegramInvalidPayload => Results.Problem(
                    title: "Stored QR payload is invalid",
                    detail: result.ErrorMessage,
                    statusCode: StatusCodes.Status500InternalServerError),
                SendQrToTelegramWorkflowStatus.TelegramRemoteApi => Results.Problem(
                    title: "Telegram API error",
                    detail: result.ErrorMessage,
                    statusCode: StatusCodes.Status502BadGateway),
                SendQrToTelegramWorkflowStatus.Success when result.Data is not null => Results.Ok(new SendQrToTelegramResponse(
                    result.Data.QrId,
                    result.Data.ChatId,
                    result.Data.SentAtUtc,
                    result.Data.Status)),
                _ => Results.Problem(statusCode: StatusCodes.Status500InternalServerError)
            };
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
