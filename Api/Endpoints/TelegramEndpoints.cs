using System.Security.Claims;
using Api.Contracts.Common;
using Api.Contracts.Telegram;
using Application.Workflows.Telegram;

namespace Api.Endpoints;

internal static class TelegramEndpoints
{
    public static RouteGroupBuilder MapTelegramEndpoints(this IEndpointRouteBuilder app)
    {
        var telegramGroup = app.MapGroup("/api/telegram")
            .WithTags("Telegram")
            .RequireAuthorization();

        telegramGroup.MapPost("/bind-chat", async (
            BindTelegramChatRequest request,
            ClaimsPrincipal principal,
            ITelegramWorkflow telegramWorkflow,
            CancellationToken ct) =>
        {
            if (!EndpointUserContext.TryGetUserId(principal, out var userId))
            {
                return Results.Unauthorized();
            }

            var result = await telegramWorkflow.BindChatAsync(userId, request.ChatId, ct);

            return result.Status switch
            {
                BindTelegramChatWorkflowStatus.InvalidChat => Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["chatId"] = ["ChatId must be greater than zero."]
                }),
                BindTelegramChatWorkflowStatus.UnauthorizedUser => Results.Unauthorized(),
                BindTelegramChatWorkflowStatus.ChatAlreadyBound => Results.Conflict(new ErrorResponse(
                    "telegram_chat_already_bound",
                    "This Telegram chat is already linked to another user.")),
                BindTelegramChatWorkflowStatus.Success when result.Data is not null => Results.Ok(new BindTelegramChatResponse(
                    result.Data.UserId,
                    result.Data.ChatId,
                    result.Data.BoundAtUtc)),
                _ => Results.Problem(statusCode: StatusCodes.Status500InternalServerError)
            };
        })
            .WithName("BindTelegramChat")
            .WithSummary("Bind Telegram chat")
            .WithDescription("Binds Telegram chatId to current user. One chat cannot be shared between users.")
            .Produces<BindTelegramChatResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status401Unauthorized);

        return telegramGroup;
    }
}
