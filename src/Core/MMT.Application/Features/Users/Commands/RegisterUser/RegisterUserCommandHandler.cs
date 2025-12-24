using MediatR;
using Microsoft.Extensions.Logging;
using MMT.Application.Common.Interfaces.Repositories;
using MMT.Domain.Entities;

namespace MMT.Application.Features.Users.Commands.RegisterUser;

public class RegisterUserCommandHandler(
    IUnitOfWork unitOfWork,
    ILogger<RegisterUserCommandHandler> logger)
    : IRequestHandler<RegisterUserCommand, RegisterUserResult>
{
    public async Task<RegisterUserResult> Handle(RegisterUserCommand request, CancellationToken ct)
    {
        try
        {
            var existingUser = await unitOfWork.Users.GetByChatIdAsync(request.ChatId, ct);
            
            if (existingUser != null)
            {
                return new RegisterUserResult
                {
                    Success = false,
                    Message = "Корбар аллакай сабт шудааст"
                };
            }
            
            var user = new User
            {
                ChatId = request.ChatId,
                Username = request.Username,
                Name = request.Name,
                PhoneNumber = request.PhoneNumber,
                City = request.City,
                Score = 0
            };
            
            await unitOfWork.Users.AddAsync(user, ct);
            
            var invitation = await unitOfWork.Invitations.GetPendingAsync(request.ChatId, ct);
            if (invitation != null)
            {
                invitation.Accept();
                unitOfWork.Invitations.Update(invitation);
                
                var inviter = await unitOfWork.Users.GetByChatIdAsync(invitation.InviterChatId, ct);
                if (inviter != null)
                {
                    inviter.UpdateScore(5);
                    unitOfWork.Users.Update(inviter);
                }
            }
            
            await unitOfWork.SaveChangesAsync(ct);
            
            logger.LogInformation("Корбар сабт шуд: ChatId={ChatId}, Name={Name}", 
                request.ChatId, request.Name);
            
            return new RegisterUserResult
            {
                Success = true,
                UserId = user.Id,
                Message = "Сабти ном муваффақ!"
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Хатогӣ дар сабти корбар: ChatId={ChatId}", request.ChatId);
            
            return new RegisterUserResult
            {
                Success = false,
                Message = "Хатогӣ рух дод"
            };
        }
    }
}
