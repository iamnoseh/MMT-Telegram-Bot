using MediatR;
using Microsoft.Extensions.Logging;
using MMT.Application.Common.Interfaces.Repositories;
using MMT.Domain.Constants;
using MMT.Domain.Entities;

namespace MMT.Application.Features.Admin.Commands.SetAdmin;

public class SetAdminCommandHandler(
    IUnitOfWork unitOfWork,
    ILogger<SetAdminCommandHandler> logger)
    : IRequestHandler<SetAdminCommand, SetAdminResult>
{
    private const string SuperAdminUsername = "iamnoseh";
    
    public async Task<SetAdminResult> Handle(SetAdminCommand request, CancellationToken ct)
    {
        var admin = await unitOfWork.Users.GetByChatIdAsync(request.AdminChatId, ct);
        if (admin == null || (!admin.IsAdmin && admin.Username?.ToLower() != SuperAdminUsername))
        {
            logger.LogWarning("Unauthorized admin access attempt by {ChatId}", request.AdminChatId);
            return new SetAdminResult
            {
                Success = false,
                Message = "Шумо ҳуқуқи admin надоред."
            };
        }
        

        User? targetUser = null;
        
        if (!string.IsNullOrEmpty(request.TargetUsername))
        {
            logger.LogInformation("Searching for user by username: {Username}", request.TargetUsername);
            var allUsers = await unitOfWork.Users.GetAllAsync(ct);
            logger.LogInformation("Total users in database: {Count}", allUsers.Count);
            
            targetUser = allUsers.FirstOrDefault(u => 
                !string.IsNullOrEmpty(u.Username) && 
                u.Username.ToLower() == request.TargetUsername.ToLower());
            
            if (targetUser == null)
            {
                var usernames = string.Join(", ", allUsers
                    .Where(u => !string.IsNullOrEmpty(u.Username))
                    .Select(u => u.Username));
                logger.LogWarning("User not found. Available usernames: {Usernames}", usernames);
            }
        }
        else if (!string.IsNullOrEmpty(request.TargetPhoneNumber))
        {
            logger.LogInformation("Searching for user by phone: {Phone}", request.TargetPhoneNumber);
            var allUsers = await unitOfWork.Users.GetAllAsync(ct);
            targetUser = allUsers.FirstOrDefault(u => 
                u.PhoneNumber == request.TargetPhoneNumber);
        }
        
        if (targetUser == null)
        {
            logger.LogWarning("Target user not found. Username: {Username}, Phone: {Phone}", 
                request.TargetUsername, request.TargetPhoneNumber);
            return new SetAdminResult
            {
                Success = false,
                Message = "Корбар ёфт нашуд."
            };
        }
        
        targetUser.IsAdmin = request.MakeAdmin;
        unitOfWork.Users.Update(targetUser);
        await unitOfWork.SaveChangesAsync(ct);
        
        logger.LogInformation(
            "Admin {AdminChatId} set admin={MakeAdmin} for user {TargetChatId} ({Username})",
            request.AdminChatId, request.MakeAdmin, targetUser.ChatId, targetUser.Username);
        
        var action = request.MakeAdmin ? "таъин" : "гирифта";
        return new SetAdminResult
        {
            Success = true,
            Message = $"Корбар {targetUser.Name} ({targetUser.Username}) admin {action} шуд."
        };
    }
}
