using MediatR;
using Microsoft.Extensions.Logging;
using MMT.Application.Common.Interfaces.Repositories;
using MMT.Domain.Constants;

namespace MMT.Application.Features.Bot.Commands.HandleNameRegistration;

public class HandleNameRegistrationCommandHandler(
    IUnitOfWork unitOfWork,
    ILogger<HandleNameRegistrationCommandHandler> logger)
    : IRequestHandler<HandleNameRegistrationCommand, HandleNameRegistrationResult>
{
    public async Task<HandleNameRegistrationResult> Handle(
        HandleNameRegistrationCommand request,
        CancellationToken ct)
    {
        logger.LogInformation("Processing name registration for ChatId: {ChatId}, Name: {Name}",
            request.ChatId, request.Name);
        
        var session = await unitOfWork.RegistrationSessions.GetActiveByChatIdAsync(request.ChatId, ct);
        
        if (session == null)
        {
            logger.LogWarning("No registration session found for ChatId: {ChatId}", request.ChatId);
            return new HandleNameRegistrationResult
            {
                Success = false,
                Message = Messages.FirstSharePhone
            };
        }
        
        session.Name = request.Name;
        session.MoveToNextStep(); // Move to City step
        unitOfWork.RegistrationSessions.Update(session);
        await unitOfWork.SaveChangesAsync(ct);
        
        logger.LogInformation("Name saved, requesting city: {ChatId}", request.ChatId);
        
        return new HandleNameRegistrationResult
        {
            Success = true,
            Message = Messages.EnterCity
        };
    }
}
