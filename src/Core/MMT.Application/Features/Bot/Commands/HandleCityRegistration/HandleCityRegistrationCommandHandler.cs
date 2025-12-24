using MediatR;
using Microsoft.Extensions.Logging;
using MMT.Application.Common.Interfaces.Repositories;
using MMT.Application.Features.Users.Commands.RegisterUser;
using MMT.Domain.Constants;

namespace MMT.Application.Features.Bot.Commands.HandleCityRegistration;

public class HandleCityRegistrationCommandHandler(
    IUnitOfWork unitOfWork,
    IMediator mediator,
    ILogger<HandleCityRegistrationCommandHandler> logger)
    : IRequestHandler<HandleCityRegistrationCommand, HandleCityRegistrationResult>
{
    public async Task<HandleCityRegistrationResult> Handle(
        HandleCityRegistrationCommand request,
        CancellationToken ct)
    {
        logger.LogInformation("Processing city registration for ChatId: {ChatId}, City: {City}",
            request.ChatId, request.City);
        
        var session = await unitOfWork.RegistrationSessions.GetActiveByChatIdAsync(request.ChatId, ct);
        
        if (session == null)
        {
            logger.LogWarning("No registration session found for ChatId: {ChatId}", request.ChatId);
            return new HandleCityRegistrationResult
            {
                Success = false,
                Message = Messages.FirstSharePhone,
                IsCompleted = false
            };
        }
        
        session.City = request.City;
        session.MoveToNextStep(); 
        
        if (!session.IsValidForCompletion())
        {
            logger.LogError("Registration session invalid: {ChatId}", request.ChatId);
            return new HandleCityRegistrationResult
            {
                Success = false,
                Message = Messages.RegistrationError,
                IsCompleted = false
            };
        }
        
        var registerCommand = new RegisterUserCommand
        {
            ChatId = session.ChatId,
            Username = session.Username ?? "",
            Name = session.Name!,
            PhoneNumber = session.PhoneNumber!,
            City = session.City!
        };
        
        var result = await mediator.Send(registerCommand, ct);
        
        if (result.Success)
        {
            unitOfWork.RegistrationSessions.Delete(session);
            await unitOfWork.SaveChangesAsync(ct);
            
            logger.LogInformation("User registered successfully: {ChatId}, Name: {Name}",
                request.ChatId, session.Name);
            
            return new HandleCityRegistrationResult
            {
                Success = true,
                Message = Messages.RegistrationSuccess,
                IsCompleted = true
            };
        }
        
        logger.LogError("Registration failed for ChatId: {ChatId}", request.ChatId);
        return new HandleCityRegistrationResult
        {
            Success = false,
            Message = Messages.RegistrationError,
            IsCompleted = false
        };
    }
}
