using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;

namespace TelegramBot.Services.Extensions
{
    public static class TelegramBotClientExtensions
    {
        public static async Task<Message> SendMessage(
            this ITelegramBotClient client,
            long chatId,
            string text,
            IReplyMarkup? replyMarkup = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                return await client.SendTextMessageAsync(
                    chatId: chatId,
                    text: text,
                    replyMarkup: replyMarkup,
                    cancellationToken: cancellationToken
                );
            }
            catch (Exception ex) when (ex.Message.Contains("Forbidden: bot was blocked by the user"))
            {
                // Log the blocked user
                Console.WriteLine($"User {chatId} has blocked the bot");
                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error sending message to {chatId}: {ex.Message}");
                throw;
            }
        }

        public static async Task<bool> IsUserBlocked(
            this ITelegramBotClient client,
            long chatId,
            CancellationToken cancellationToken = default)
        {
            try
            {
                await client.SendChatActionAsync(chatId, Telegram.Bot.Types.Enums.ChatAction.Typing, cancellationToken: cancellationToken);
                return false;
            }
            catch (Exception ex) when (ex.Message.Contains("Forbidden: bot was blocked by the user"))
            {
                return true;
            }
        }

        public static async Task<bool> HandleBlockedUser(
            this ITelegramBotClient client,
            IServiceProvider serviceProvider,
            long chatId,
            CancellationToken cancellationToken = default)
        {
            if (await client.IsUserBlocked(chatId, cancellationToken))
            {
                using var scope = serviceProvider.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<DataContext>();

                var user = await dbContext.Users.FirstOrDefaultAsync(u => u.ChatId == chatId, cancellationToken);
                if (user != null)
                {
                    user.IsActive = false;
                    await dbContext.SaveChangesAsync(cancellationToken);
                }
                return true;
            }
            return false;
        }
    }
}
