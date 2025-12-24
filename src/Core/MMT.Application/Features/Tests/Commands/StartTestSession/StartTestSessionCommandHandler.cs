using MediatR;
using Microsoft.Extensions.Logging;
using MMT.Application.Common.Interfaces.Repositories;
using MMT.Domain.Entities;

namespace MMT.Application.Features.Tests.Commands.StartTestSession;

public class StartTestSessionCommandHandler(
    IUnitOfWork unitOfWork,
    ILogger<StartTestSessionCommandHandler> logger)
    : IRequestHandler<StartTestSessionCommand, StartTestSessionResult>
{
    public async Task<StartTestSessionResult> Handle(StartTestSessionCommand request, CancellationToken ct)
    {
        var activeSession = await unitOfWork.TestSessions.GetActiveByUserAsync(request.ChatId, ct);
        
        if (activeSession != null)
        {
            activeSession.CompleteSession();
            unitOfWork.TestSessions.Update(activeSession);
        }
        
        var subject = await unitOfWork.Subjects.GetByIdAsync(request.SubjectId, ct);
        if (subject == null)
        {
            return new StartTestSessionResult
            {
                Success = false,
                Message = "Фан ёфт нашуд"
            };
        }
        
        var question = await unitOfWork.Questions.GetRandomBySubjectAsync(request.SubjectId, ct);
        if (question == null)
        {
            return new StartTestSessionResult
            {
                Success = false,
                Message = "Саволҳо дастрас нестанд"
            };
        }
        
        var session = new UserTestSession
        {
            ChatId = request.ChatId,
            SubjectId = request.SubjectId,
            CurrentQuestionNumber = 1,
            CurrentQuestionId = question.Id,
            Score = 0,
            IsActive = true
        };
        
        await unitOfWork.TestSessions.AddAsync(session, ct);
        await unitOfWork.SaveChangesAsync(ct);
        
        logger.LogInformation("Test session started: ChatId={ChatId}, Subject={Subject}, Session={SessionId}",
            request.ChatId, subject.Name, session.Id);
        
        return new StartTestSessionResult
        {
            Success = true,
            SessionId = session.Id,
            FirstQuestion = new QuestionDto
            {
                Id = question.Id,
                Text = question.QuestionText,
                OptionA = question.Option.OptionA,
                OptionB = question.Option.OptionB,
                OptionC = question.Option.OptionC,
                OptionD = question.Option.OptionD,
                SubjectId = subject.Id,
                SubjectName = subject.Name
            }
        };
    }
}
