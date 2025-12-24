using MediatR;
using Microsoft.Extensions.Logging;
using MMT.Application.Common.Interfaces.Repositories;
using MMT.Domain.Constants;

namespace MMT.Application.Features.Bot.Commands.SelectSubject;

public class SelectSubjectCommandHandler(
    IUnitOfWork unitOfWork,
    ILogger<SelectSubjectCommandHandler> logger)
    : IRequestHandler<SelectSubjectCommand, SelectSubjectResult>
{
    public async Task<SelectSubjectResult> Handle(SelectSubjectCommand request, CancellationToken ct)
    {
        logger.LogInformation("Selecting subject for ChatId: {ChatId}, SubjectId: {SubjectId}",
            request.ChatId, request.SubjectId);
        
        var subject = await unitOfWork.Subjects.GetByIdAsync(request.SubjectId, ct);
        if (subject == null)
        {
            logger.LogWarning("Subject not found: {SubjectId}", request.SubjectId);
            return new SelectSubjectResult
            {
                Success = false,
                Message = "Фан ёфт нашуд!"
            };
        }
        
        var userState = await unitOfWork.UserStates.GetOrCreateAsync(request.ChatId, ct);
        userState.SelectSubject(request.SubjectId);
        unitOfWork.UserStates.Update(userState);
        await unitOfWork.SaveChangesAsync(ct);
        
        logger.LogInformation("Subject selected: {ChatId} -> {SubjectName}", request.ChatId, subject.Name);
        
        return new SelectSubjectResult
        {
            Success = true,
            Message = string.Format(Messages.SubjectSelected, subject.Name),
            SubjectName = subject.Name
        };
    }
}
