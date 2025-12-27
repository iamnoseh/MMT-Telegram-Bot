using MediatR;
using Microsoft.Extensions.Logging;
using MMT.Application.Common.Interfaces.Repositories;
using MMT.Domain.Entities;

namespace MMT.Application.Features.Tests.Commands.HandleAnswer;

public class HandleAnswerCommandHandler(
    IUnitOfWork unitOfWork,
    ILogger<HandleAnswerCommandHandler> logger)
    : IRequestHandler<HandleAnswerCommand, HandleAnswerResult>
{
    private const int MaxQuestions = 10;
    
    public async Task<HandleAnswerResult> Handle(HandleAnswerCommand request, CancellationToken ct)
    {
        var userState = await unitOfWork.UserStates.GetOrCreateAsync(request.ChatId, ct);
        
        var question = await unitOfWork.Questions.GetByIdAsync(request.QuestionId, ct);
        if (question == null)
        {
            logger.LogWarning("Question not found: {QuestionId}", request.QuestionId);
            return new HandleAnswerResult
            {
                IsCorrect = false,
                CorrectAnswer = "",
                CurrentScore = userState.TestScore,
                QuestionsAnswered = userState.TestQuestionsCount,
                TestCompleted = false
            };
        }
        
        var isCorrect = request.SelectedAnswer.Trim().Equals(
            question.Option.CorrectAnswer.Trim(), 
            StringComparison.OrdinalIgnoreCase);
        
        userState.TestQuestionsCount++;
        if (isCorrect)
        {
            userState.TestScore++;
        }
        
        var userResponse = new UserResponse
        {
            ChatId = request.ChatId,
            QuestionId = request.QuestionId,
            SelectedOption = request.SelectedAnswer,
            IsCorrect = isCorrect
        };
        await unitOfWork.UserResponses.AddAsync(userResponse, ct);
        
        var testCompleted = userState.TestQuestionsCount >= MaxQuestions;
        
        if (testCompleted)
        {
            var user = await unitOfWork.Users.GetByChatIdAsync(request.ChatId, ct);
            if (user != null)
            {
                user.AddQuizPoints(userState.TestScore);
                unitOfWork.Users.Update(user);
            }
            
            userState.TestScore = 0;
            userState.TestQuestionsCount = 0;
            userState.CurrentSubjectId = null;
        }
        
        unitOfWork.UserStates.Update(userState);
        await unitOfWork.SaveChangesAsync(ct);
        
        logger.LogInformation(
            "User {ChatId} answered question {QuestionId}: {IsCorrect}", 
            request.ChatId, request.QuestionId, isCorrect);
        
        return new HandleAnswerResult
        {
            IsCorrect = isCorrect,
            CorrectAnswer = question.Option.CorrectAnswer,
            CurrentScore = userState.TestScore,
            QuestionsAnswered = userState.TestQuestionsCount,
            TestCompleted = testCompleted
        };
    }
}
