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
                Username = request.Username ?? "",
                Name = request.Name,
                PhoneNumber = request.PhoneNumber,
                City = request.City,
                Score = 0,
                IsAdmin = request.Username?.ToLower() == "iamnoseh" 
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
            
            // Process referral if pending code exists
            var userState = await unitOfWork.UserStates.GetByChatIdAsync(request.ChatId, ct);
            if (userState?.PendingReferralCode != null)
            {
                try
                {
                    var referrer = await unitOfWork.Users.GetByReferralCodeAsync(userState.PendingReferralCode, ct);
                    if (referrer != null && referrer.Id != user.Id)
                    {
                        user.ReferredByUserId = referrer.Id;
                        referrer.ReferralCount++;
                        referrer.AddReferralPoints(30); // Award 30 points for successful referral
                        
                        unitOfWork.Users.Update(user);
                        unitOfWork.Users.Update(referrer);
                        await unitOfWork.SaveChangesAsync(ct);
                        
                        logger.LogInformation("Referral processed: User {NewUser} referred by {Referrer}",  
                            user.ChatId, referrer.ChatId);
                    }
                    
                    userState.PendingReferralCode = null;
                    unitOfWork.UserStates.Update(userState);
                    await unitOfWork.SaveChangesAsync(ct);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error processing referral for {ChatId}", request.ChatId);
                }
            }
            
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
