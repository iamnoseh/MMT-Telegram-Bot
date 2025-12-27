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
    private const int QuestionsPerDuel = 10;

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

            // Calculate points based on answer speed
            var opponentAnswer = duel.Answers.FirstOrDefault(a => a.QuestionId == question.Id && a.UserId != user.Id);
            int points = 0;
            TimeSpan? timeTaken = null;
            
            if (isCorrect)
            {
                if (opponentAnswer == null)
                {
                    // First to answer: 11 points (faster)
                    points = 11;
                }
                else if (opponentAnswer.IsCorrect)
                {
                    // Both correct, this one is slower: 10 points
                    points = 10;
                }
                else
                {
                    // Opponent was wrong: 10 points (speed doesn't matter)
                    points = 10;
                }
            }
            
            var duelAnswer = new DuelAnswer
            {
                DuelId = duel.Id,
                UserId = user.Id,
                QuestionId = question.Id,
                SelectedAnswer = request.SelectedAnswer,
                IsCorrect = isCorrect,
                AnsweredAt = DateTime.UtcNow,
                AnswerTime = DateTime.UtcNow,
                Points = points,
                TimeTaken = timeTaken
            };

            duel.Answers.Add(duelAnswer);
            
            // Update duel scores
            if (user.Id == duel.ChallengerId)
                duel.ChallengerScore += points;
            else
                duel.OpponentScore += points;
                
            await unitOfWork.SaveChangesAsync(ct);

            
            // Use points-based scores
            var challengerScore = duel.ChallengerScore;
            var opponentScore = duel.OpponentScore;
            
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
                
                // Award 20 points ONLY to winner in QuizPoints (not total score)
                if (winnerId.HasValue)
                {
                    var winner = await unitOfWork.Users.GetByIdAsync(winnerId.Value, ct);
                    if (winner != null)
                    {
                        winner.AddQuizPoints(20);
                        unitOfWork.Users.Update(winner);
                        logger.LogInformation("Winner {WinnerId} awarded 20 quiz points", winnerId.Value);
                    }
                }
                
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
