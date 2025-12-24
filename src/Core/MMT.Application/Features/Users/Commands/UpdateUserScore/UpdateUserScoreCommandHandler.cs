using MediatR;
using Microsoft.Extensions.Logging;
using MMT.Application.Common.Interfaces.Repositories;

namespace MMT.Application.Features.Users.Commands.UpdateUserScore;

public class UpdateUserScoreCommandHandler(
    IUnitOfWork unitOfWork,
    ILogger<UpdateUserScoreCommandHandler> logger) 
    : IRequestHandler<UpdateUserScoreCommand, bool>
{
    public async Task<bool> Handle(UpdateUserScoreCommand request, CancellationToken ct)
    {
        var user = await unitOfWork.Users.GetByChatIdAsync(request.ChatId, ct);
        
        if (user == null)
        {
            logger.LogWarning("User not found: ChatId={ChatId}", request.ChatId);
            return false;
        }
        
        user.UpdateScore(request.Points);
        unitOfWork.Users.Update(user);
        await unitOfWork.SaveChangesAsync(ct);
        
        logger.LogInformation("User score updated: ChatId={ChatId}, Points={Points}, NewScore={Score}", 
            request.ChatId, request.Points, user.Score);
        
        return true;
    }
}
