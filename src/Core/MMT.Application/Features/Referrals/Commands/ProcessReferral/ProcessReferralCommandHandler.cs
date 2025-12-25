using MediatR;
using Microsoft.Extensions.Logging;
using MMT.Application.Common.Interfaces.Repositories;

namespace MMT.Application.Features.Referrals.Commands.ProcessReferral;

public class ProcessReferralCommandHandler(
    IUnitOfWork unitOfWork,
    ILogger<ProcessReferralCommandHandler> logger)
    : IRequestHandler<ProcessReferralCommand, ProcessReferralResult>
{
    public async Task<ProcessReferralResult> Handle(ProcessReferralCommand request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.ReferralCode))
        {
            return new ProcessReferralResult
            {
                Success = false,
                Message = "Коди даъват нодуруст аст."
            };
        }
        
        // Find referrer by code
        var referrer = await unitOfWork.Users.GetByReferralCodeAsync(request.ReferralCode, ct);
        
        if (referrer == null)
        {
            logger.LogWarning("Referral code not found: {Code}", request.ReferralCode);
            return new ProcessReferralResult
            {
                Success = false,
                Message = "Коди даъват ёфт нашуд."
            };
        }
        
        // Get new user
        var newUser = await unitOfWork.Users.GetByChatIdAsync(request.NewUserChatId, ct);
        
        if (newUser == null)
        {
            logger.LogWarning("New user not found: {ChatId}", request.NewUserChatId);
            return new ProcessReferralResult
            {
                Success = false,
                Message = "Корбар ёфт нашуд."
            };
        }
        
        // Check if user already referred
        if (newUser.ReferredByUserId != null)
        {
            logger.LogInformation("User {ChatId} already referred by {ReferrerId}", 
                request.NewUserChatId, newUser.ReferredByUserId);
            return new ProcessReferralResult
            {
                Success = false,
                Message = "Шумо аллакай тавассути дигар корбар даъват шудаед."
            };
        }
        
        // Can't refer yourself
        if (referrer.Id == newUser.Id)
        {
            return new ProcessReferralResult
            {
                Success = false,
                Message = "Шумо наметавонед худатонро даъват кунед."
            };
        }
        
        // Process referral
        newUser.ReferredByUserId = referrer.Id;
        referrer.ReferralCount++;
        
        unitOfWork.Users.Update(newUser);
        unitOfWork.Users.Update(referrer);
        await unitOfWork.SaveChangesAsync(ct);
        
        logger.LogInformation("Referral processed: User {NewUser} referred by {Referrer}", 
            newUser.ChatId, referrer.ChatId);
        
        return new ProcessReferralResult
        {
            Success = true,
            Message = $"Шумо тавассути {referrer.Name} даъват шудед!",
            ReferrerName = referrer.Name
        };
    }
}
