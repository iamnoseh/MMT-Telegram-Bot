using MediatR;
using Microsoft.Extensions.Logging;
using MMT.Application.Common.Interfaces.Repositories;
using MMT.Domain.Entities;

namespace MMT.Application.Features.Duels.Commands.SubmitDuelAnswer;

public class SubmitDuelAnswerCommandHandler(
    IUnitOfWork unitOfWork,
    ILogger<SubmitDuelAnswerCommandHandler> logger)
    : IRequestHandler<SubmitDuelAnswerCommand, SubmitDuelAnswerResult>
{
    private const int QuestionsPerDuel = 5;

    public async Task<SubmitDuelAnswerResult> Handle(SubmitDuelAnswerCommand request, CancellationToken ct)
    {
        try
        {
            var duel = await unitOfWork.Duels.GetByIdWithDetailsAsync(request.DuelId, ct);
            if (duel == null)
            {
                return new SubmitDuelAnswerResult
                {
                    Success = false,
                    Message = "Дуэл ёфт нашуд."
                };
            }

            if (duel.Status != DuelStatus.Active)
            {
                return new SubmitDuelAnswerResult
                {
                    Success = false,
                    Message = "Ин дуэл фаъол нест."
                };
            }

            var user = await unitOfWork.Users.GetByChatIdAsync(request.UserChatId, ct);
            if (user == null || (user.Id != duel.ChallengerId && user.Id != duel.OpponentId))
            {
                return new SubmitDuelAnswerResult
                {
                    Success = false,
                    Message = "Шумо дар ин дуэл иштирок намекунед."
                };
            }

            var question = await unitOfWork.Questions.GetByIdAsync(request.QuestionId, ct);
            if (question == null)
            {
                return new SubmitDuelAnswerResult
                {
                    Success = false,
                    Message = "Савол ёфт нашуд."
                };
            }

            var isCorrect = question.Option.CorrectAnswer.Equals(request.SelectedAnswer, StringComparison.OrdinalIgnoreCase);

            var duelAnswer = new DuelAnswer
            {
                DuelId = duel.Id,
                UserId = user.Id,
                QuestionId = question.Id,
                SelectedAnswer = request.SelectedAnswer,
                IsCorrect = isCorrect,
                AnsweredAt = DateTime.UtcNow
            };

            await unitOfWork.SaveChangesAsync(ct);

            
            var challengerScore = duel.Answers.Count(a => a.UserId == duel.ChallengerId && a.IsCorrect);
            var opponentScore = duel.Answers.Count(a => a.UserId == duel.OpponentId && a.IsCorrect);
            
            var userScore = user.Id == duel.ChallengerId ? challengerScore : opponentScore;
            var otherScore = user.Id == duel.ChallengerId ? opponentScore : challengerScore;

            var challengerAnswers = duel.Answers.Count(a => a.UserId == duel.ChallengerId);
            var opponentAnswers = duel.Answers.Count(a => a.UserId == duel.OpponentId);
            var duelCompleted = challengerAnswers >= QuestionsPerDuel && opponentAnswers >= QuestionsPerDuel;

            int? winnerId = null;
            if (duelCompleted)
            {
                if (challengerScore > opponentScore)
                    winnerId = duel.ChallengerId;
                else if (opponentScore > challengerScore)
                    winnerId = duel.OpponentId;
        

                duel.Status = DuelStatus.Completed;
                duel.WinnerId = winnerId;
                duel.CompletedAt = DateTime.UtcNow;
                unitOfWork.Duels.Update(duel);
                await unitOfWork.SaveChangesAsync(ct);
            }

            logger.LogInformation("Answer submitted for duel {DuelId} by user {UserId}, correct: {IsCorrect}",
                duel.Id, user.Id, isCorrect);

            return new SubmitDuelAnswerResult
            {
                Success = true,
                IsCorrect = isCorrect,
                CorrectAnswer = question.Option.CorrectAnswer,
                UserScore = userScore,
                OpponentScore = otherScore,
                DuelCompleted = duelCompleted,
                WinnerId = winnerId,
                Message = isCorrect ? "Дуруст!" : "Нодуруст!"
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error submitting duel answer for duel {DuelId}", request.DuelId);
            return new SubmitDuelAnswerResult
            {
                Success = false,
                Message = "Хатогӣ ҳангоми сабти ҷавоб."
            };
        }
    }
}
