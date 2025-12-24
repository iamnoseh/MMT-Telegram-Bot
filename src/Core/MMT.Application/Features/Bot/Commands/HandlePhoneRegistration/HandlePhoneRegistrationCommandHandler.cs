using MediatR;
using Microsoft.Extensions.Logging;
using MMT.Application.Common.Interfaces.Repositories;
using MMT.Domain.Constants;
using MMT.Domain.Entities;

namespace MMT.Application.Features.Bot.Commands.HandlePhoneRegistration;

public class HandlePhoneRegistrationCommandHandler(
    IUnitOfWork unitOfWork,
    ILogger<HandlePhoneRegistrationCommandHandler> logger)
    : IRequestHandler<HandlePhoneRegistrationCommand, HandlePhoneRegistrationResult>
{
    public async Task<HandlePhoneRegistrationResult> Handle(
        HandlePhoneRegistrationCommand request, 
        CancellationToken ct)
    {
        logger.LogInformation("Processing phone registration for ChatId: {ChatId}, Phone: {Phone}", 
            request.ChatId, request.PhoneNumber);
        
        var existingUser = await unitOfWork.Users.GetByChatIdAsync(request.ChatId, ct);
        if (existingUser != null)
        {
            logger.LogWarning("User already registered: {ChatId}", request.ChatId);
            return new HandlePhoneRegistrationResult
            {
                Success = false,
                Message = Messages.AlreadyRegistered,
                NextStep = RegistrationStep.Completed
            };
        }
        
        var session = await unitOfWork.RegistrationSessions.GetActiveByChatIdAsync(request.ChatId, ct);
        
        if (session == null)
        {
            session = new RegistrationSession
            {
                ChatId = request.ChatId,
                Username = request.Username,
                PhoneNumber = request.PhoneNumber,
                CurrentStep = Domain.Entities.RegistrationStep.Phone
            };
            
            await unitOfWork.RegistrationSessions.AddAsync(session, ct);
        }
        else
        {
            session.PhoneNumber = request.PhoneNumber;
            session.Username = request.Username;
            unitOfWork.RegistrationSessions.Update(session);
        }
        
        session.MoveToNextStep(); // Move to Name step
        await unitOfWork.SaveChangesAsync(ct);
        
        logger.LogInformation("Phone saved, requesting name: {ChatId}", request.ChatId);
        
        return new HandlePhoneRegistrationResult
        {
            Success = true,
            Message = Messages.ThankYouEnterName,
            NextStep = RegistrationStep.Name
        };
    }
}
