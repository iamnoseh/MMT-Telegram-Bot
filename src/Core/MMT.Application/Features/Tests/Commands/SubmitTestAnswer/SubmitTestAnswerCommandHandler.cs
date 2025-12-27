using MediatR;
using Microsoft.Extensions.Logging;
using MMT.Application.Common.Interfaces.Repositories;
using MMT.Application.Features.Tests.Commands.StartTestSession;
using MMT.Domain.Constants;
using MMT.Domain.Entities;

namespace MMT.Application.Features.Tests.Commands.SubmitTestAnswer;

public class SubmitTestAnswerCommandHandler(
    IUnitOfWork unitOfWork,
    ILogger<SubmitTestAnswerCommandHandler> logger)
    : IRequestHandler<SubmitTestAnswerCommand, SubmitTestAnswerResult>
{
    public async Task<SubmitTestAnswerResult> Handle(SubmitTestAnswerCommand request, CancellationToken ct)
    {
        var session = await unitOfWork.TestSessions.GetActiveByUserAsync(request.ChatId, ct);
        
        if (session == null)
        {
            return new SubmitTestAnswerResult
            {
                Success = false,
                Message = "Тест ёфт нашуд"
            };
        }
        
        var question = await unitOfWork.Questions.GetByIdAsync(request.QuestionId, ct);
        if (question == null)
        {
            return new SubmitTestAnswerResult
            {
                Success = false,
                Message = "Савол ёфт нашуд"
            };
        }
        
        var isCorrect = question.Option.CorrectAnswer.Equals(request.SelectedOption, 
            StringComparison.OrdinalIgnoreCase);
        
        var userResponse = new UserResponse
        {
            ChatId = request.ChatId,
            QuestionId = request.QuestionId,
            SelectedOption = request.SelectedOption,
            IsCorrect = isCorrect
        };
        
        await unitOfWork.Questions.GetByIdAsync(0, ct);
        session.AnswerQuestion(isCorrect);
        
        if (isCorrect)
        {
            var user = await unitOfWork.Users.GetByChatIdAsync(request.ChatId, ct);
            if (user != null)
            {
                user.UpdateScore(1);
                unitOfWork.Users.Update(user);
            }
        }
        
        var testCompleted = session.CurrentQuestionNumber > BotConstants.MaxQuestions;
        
        QuestionDto? nextQuestion = null;
        
        if (!testCompleted)
        {
            var next = await unitOfWork.Questions.GetRandomBySubjectAsync(session.SubjectId, ct);
            if (next != null)
            {
                session.CurrentQuestionId = next.Id;
                nextQuestion = new QuestionDto
                {
                    Id = next.Id,
                    Text = next.QuestionText,
                    OptionA = next.Option.OptionA,
                    OptionB = next.Option.OptionB,
                    OptionC = next.Option.OptionC,
                    OptionD = next.Option.OptionD,
                    SubjectId = next.SubjectId,
                    SubjectName = next.Subject.Name
                };
            }
        }
        else
        {
            session.CompleteSession();
        }
        
        unitOfWork.TestSessions.Update(session);
        await unitOfWork.SaveChangesAsync(ct);
        
        logger.LogInformation("Answer submitted: ChatId={ChatId}, Question={QuestionId}, Correct={IsCorrect}",
            request.ChatId, request.QuestionId, isCorrect);
        
        return new SubmitTestAnswerResult
        {
            Success = true,
            IsCorrect = isCorrect,
            CorrectAnswer = question.Option.CorrectAnswer,
            NewScore = session.Score,
            QuestionNumber = session.CurrentQuestionNumber,
            TestCompleted = testCompleted,
            NextQuestion = nextQuestion
        };
    }
}
