using MediatR;
using Microsoft.Extensions.Logging;
using MMT.Application.Common.Interfaces.Repositories;
using MMT.Domain.Constants;

namespace MMT.Application.Features.Bot.Commands.HandleStart;

public class HandleStartCommandHandler(
    IUnitOfWork unitOfWork,
    ILogger<HandleStartCommandHandler> logger)
    : IRequestHandler<HandleStartCommand, HandleStartResult>
{
    public async Task<HandleStartResult> Handle(HandleStartCommand request, CancellationToken ct)
    {
        logger.LogInformation("Handling /start command for ChatId: {ChatId}", request.ChatId);
        
        var user = await unitOfWork.Users.GetByChatIdAsync(request.ChatId, ct);
        
        if (user != null)
        {
            logger.LogInformation("User already registered: {ChatId}, Name: {Name}", request.ChatId, user.Name);
            
            return new HandleStartResult
            {
                IsRegistered = true,
                Message = $"Салом, {user.Name}! 👋\n\nШумо аллакай қайд шудаед.\n\n" +
                         $"Холи ҳозира холҳоятон: {user.Score} 🏆\n\n" +
                         "Барои оғози тест тугмаҳоро пахш кунед!",
                ShouldRequestPhone = false
            };
        }
        
        logger.LogInformation("New user, requesting registration: {ChatId}", request.ChatId);
        
        return new HandleStartResult
        {
            IsRegistered = false,
            Message = Messages.WelcomeMessage,
            ShouldRequestPhone = true
        };
    }
}
