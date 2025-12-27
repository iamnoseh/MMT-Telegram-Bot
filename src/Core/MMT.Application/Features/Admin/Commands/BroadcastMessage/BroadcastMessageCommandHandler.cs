using MediatR;
using Microsoft.Extensions.Logging;
using MMT.Application.Common.Interfaces.Repositories;

namespace MMT.Application.Features.Admin.Commands.BroadcastMessage;

public class BroadcastMessageCommandHandler(
    IUnitOfWork unitOfWork,
    ILogger<BroadcastMessageCommandHandler> logger)
    : IRequestHandler<BroadcastMessageCommand, BroadcastMessageResult>
{
    public async Task<BroadcastMessageResult> Handle(BroadcastMessageCommand request, CancellationToken ct)
    {
        try
        {
            var admin = await unitOfWork.Users.GetByChatIdAsync(request.AdminChatId, ct);
            if (admin == null || !admin.IsAdmin)
            {
                return new BroadcastMessageResult
                {
                    Success = false,
                    Message = "Шумо ҳуқуқи паём фиристодан надоред."
                };
            }

            var users = await unitOfWork.Users.GetAllAsync(ct);

            logger.LogInformation("Broadcasting to {Count} users", users.Count);

            return new BroadcastMessageResult
            {
                Success = true,
                TotalUsers = users.Count,
                SuccessCount = 0,
                FailureCount = 0,
                Message = $"Омода барои фиристодан ба {users.Count} корбар."
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error preparing broadcast");
            return new BroadcastMessageResult
            {
                Success = false,
                Message = "Хатогӣ ҳангоми омодасозӣ."
            };
        }
    }
}
