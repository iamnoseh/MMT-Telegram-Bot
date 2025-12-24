using MediatR;
using Microsoft.Extensions.Logging;
using MMT.Application.Common.Interfaces.Repositories;

namespace MMT.Application.Features.Users.Commands.ChangeUserName;

public class ChangeUserNameCommandHandler(
    IUnitOfWork unitOfWork,
    ILogger<ChangeUserNameCommandHandler> logger)
    : IRequestHandler<ChangeUserNameCommand, ChangeUserNameResult>
{
    public async Task<ChangeUserNameResult> Handle(ChangeUserNameCommand request, CancellationToken ct)
    {
        var user = await unitOfWork.Users.GetByChatIdAsync(request.ChatId, ct);
        
        if (user == null)
        {
            logger.LogWarning("User not found: {ChatId}", request.ChatId);
            return new ChangeUserNameResult
            {
                Success = false,
                Message = "Корбар ёфт нашуд."
            };
        }
        
        if (user.HasChangedName)
        {
            logger.LogInformation("User {ChatId} already changed name", request.ChatId);
            return new ChangeUserNameResult
            {
                Success = false,
                Message = "Шумо аллакай як бор номи худро иваз кардаед."
            };
        }
        
        user.Name = request.NewName;
        user.HasChangedName = true;
        
        unitOfWork.Users.Update(user);
        await unitOfWork.SaveChangesAsync(ct);
        
        logger.LogInformation("User {ChatId} changed name to {NewName}", request.ChatId, request.NewName);
        
        return new ChangeUserNameResult
        {
            Success = true,
            Message = $"Номи шумо ба '{request.NewName}' иваз шуд!"
        };
    }
}
