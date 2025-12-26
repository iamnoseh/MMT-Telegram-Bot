using MediatR;

namespace MMT.Application.Features.Duels.Commands.SubmitDuelAnswer;

public record SubmitDuelAnswerCommand : IRequest<SubmitDuelAnswerResult>
{
    public int DuelId { get; init; }
    public long UserChatId { get; init; }
    public int QuestionId { get; init; }
    public string SelectedAnswer { get; init; } = string.Empty;
}

public record SubmitDuelAnswerResult
{
    public bool Success { get; init; }
    public bool IsCorrect { get; init; }
    public string CorrectAnswer { get; init; } = string.Empty;
    public int UserScore { get; init; }
    public int OpponentScore { get; init; }
    public bool DuelCompleted { get; init; }
    public int? WinnerId { get; init; }
    public string Message { get; init; } = string.Empty;
}
