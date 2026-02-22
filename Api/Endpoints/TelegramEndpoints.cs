using System.Security.Claims;
using Api.Contracts.Common;
using Api.Contracts.Telegram;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

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
            AppDbContext db,
            CancellationToken ct) =>
        {
            if (request.ChatId <= 0)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["chatId"] = ["ChatId must be greater than zero."]
                });
            }

            if (!EndpointUserContext.TryGetUserId(principal, out var userId))
            {
                return Results.Unauthorized();
            }

            var userExists = await db.Users.AnyAsync(x => x.Id == userId, ct);
            if (!userExists)
            {
                return Results.Unauthorized();
            }

            var existingByChat = await db.TelegramBindings
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.ChatId == request.ChatId, ct);

            if (existingByChat is not null && existingByChat.UserId != userId)
            {
                return Results.Conflict(new ErrorResponse(
                    "telegram_chat_already_bound",
                    "This Telegram chat is already linked to another user."));
            }

            var now = DateTimeOffset.UtcNow;

            var binding = await db.TelegramBindings
                .FirstOrDefaultAsync(x => x.UserId == userId, ct);

            if (binding is null)
            {
                binding = new TelegramBinding
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    ChatId = request.ChatId,
                    CreatedAt = now
                };

                db.TelegramBindings.Add(binding);
            }
            else
            {
                binding.ChatId = request.ChatId;
                binding.CreatedAt = now;
            }

            await db.SaveChangesAsync(ct);

            return Results.Ok(new BindTelegramChatResponse(
                binding.UserId,
                binding.ChatId,
                binding.CreatedAt));
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
