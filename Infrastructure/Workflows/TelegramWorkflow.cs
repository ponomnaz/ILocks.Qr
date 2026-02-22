using Application.Workflows.Telegram;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Workflows;

public sealed class TelegramWorkflow(AppDbContext db) : ITelegramWorkflow
{
    public async Task<BindTelegramChatWorkflowResult> BindChatAsync(Guid userId, long chatId, CancellationToken ct)
    {
        if (chatId <= 0)
        {
            return new BindTelegramChatWorkflowResult(BindTelegramChatWorkflowStatus.InvalidChat);
        }

        var userExists = await db.Users.AnyAsync(x => x.Id == userId, ct);
        if (!userExists)
        {
            return new BindTelegramChatWorkflowResult(BindTelegramChatWorkflowStatus.UnauthorizedUser);
        }

        var existingByChat = await db.TelegramBindings
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.ChatId == chatId, ct);

        if (existingByChat is not null && existingByChat.UserId != userId)
        {
            return new BindTelegramChatWorkflowResult(BindTelegramChatWorkflowStatus.ChatAlreadyBound);
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
                ChatId = chatId,
                CreatedAt = now
            };

            db.TelegramBindings.Add(binding);
        }
        else
        {
            binding.ChatId = chatId;
            binding.CreatedAt = now;
        }

        await db.SaveChangesAsync(ct);

        return new BindTelegramChatWorkflowResult(
            BindTelegramChatWorkflowStatus.Success,
            new BindTelegramChatWorkflowData(binding.UserId, binding.ChatId, binding.CreatedAt));
    }
}
