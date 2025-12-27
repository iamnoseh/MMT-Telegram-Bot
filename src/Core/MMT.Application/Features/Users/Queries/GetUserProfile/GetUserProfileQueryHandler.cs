using MediatR;
using Microsoft.Extensions.Logging;
using MMT.Application.Common.Interfaces.Repositories;

namespace MMT.Application.Features.Users.Queries.GetUserProfile;

public class GetUserProfileQueryHandler(
    IUnitOfWork unitOfWork,
    ILogger<GetUserProfileQueryHandler> logger)
    : IRequestHandler<GetUserProfileQuery, UserProfileDto?>
{
    public async Task<UserProfileDto?> Handle(GetUserProfileQuery request, CancellationToken ct)
    {
        var user = await unitOfWork.Users.GetByChatIdAsync(request.ChatId, ct);
        
        if (user == null)
        {
            logger.LogWarning("User not found: {ChatId}", request.ChatId);
            return null;
        }
        
        var allUsers = await unitOfWork.Users.GetAllAsync(ct);
        var rankedUsers = allUsers.OrderByDescending(u => u.Score).ToList();
        var rank = rankedUsers.FindIndex(u => u.ChatId == request.ChatId) + 1;
        
        var level = user.Score switch
        {
            <= 150 => 1,
            <= 300 => 2,
            <= 450 => 3,
            <= 600 => 4,
            _ => 5
        };
        
        return new UserProfileDto
        {
            Name = user.Name,
            City = user.City,
            Score = user.Score,
            QuizPoints = user.QuizPoints,
            ReferralPoints = user.ReferralPoints,
            ReferralCount = user.ReferralCount,
            Level = level,
            Rank = rank,
            PhoneNumber = user.PhoneNumber,
            HasChangedName = user.HasChangedName
        };
    }
}
