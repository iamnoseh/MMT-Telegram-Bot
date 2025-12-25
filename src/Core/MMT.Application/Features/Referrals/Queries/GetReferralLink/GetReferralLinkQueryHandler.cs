using MediatR;
using Microsoft.Extensions.Logging;
using MMT.Application.Common.Interfaces.Repositories;

namespace MMT.Application.Features.Referrals.Queries.GetReferralLink;

public class GetReferralLinkQueryHandler(
    IUnitOfWork unitOfWork,
    ILogger<GetReferralLinkQueryHandler> logger)
    : IRequestHandler<GetReferralLinkQuery, ReferralLinkResult>
{
    
    public async Task<ReferralLinkResult> Handle(GetReferralLinkQuery request, CancellationToken ct)
    {
        var user = await unitOfWork.Users.GetByChatIdAsync(request.ChatId, ct);
        
        if (user == null)
        {
            logger.LogWarning("User not found for ChatId {ChatId}", request.ChatId);
            return new ReferralLinkResult
            {
                ReferralCode = "",
                ReferralLink = "",
                TotalReferrals = 0
            };
        }
        
        if (string.IsNullOrEmpty(user.ReferralCode))
        {
            user.ReferralCode = GenerateReferralCode(user);
            unitOfWork.Users.Update(user);
            await unitOfWork.SaveChangesAsync(ct);
            
            logger.LogInformation("Generated referral code {Code} for user {ChatId}", 
                user.ReferralCode, request.ChatId);
        }
        
        var referralLink = $"https://t.me/{request.BotUsername}?start=ref_{user.ReferralCode}";
        
        return new ReferralLinkResult
        {
            ReferralCode = user.ReferralCode,
            ReferralLink = referralLink,
            TotalReferrals = user.ReferralCount
        };
    }
    
    private static string GenerateReferralCode(Domain.Entities.User user)
    {
        var baseCode = !string.IsNullOrEmpty(user.Username) 
            ? user.Username.Replace("@", "").ToLower()
            : user.ChatId.ToString();
            
        var random = Guid.NewGuid().ToString("N")[..6].ToUpper();
        return $"{baseCode}_{random}";
    }
}
