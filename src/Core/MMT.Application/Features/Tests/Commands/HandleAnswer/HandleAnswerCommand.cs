using MediatR;

namespace MMT.Application.Features.Tests.Commands.HandleAnswer;

public record HandleAnswerCommand : IRequest<HandleAnswerResult>
{
    public long ChatId { get; init; }
    public int QuestionId { get; init; }
    public string SelectedAnswer { get; init; } = string.Empty;
}

public record HandleAnswerResult
{
    public bool IsCorrect { get; init; }
    public string CorrectAnswer { get; init; } = string.Empty;
    public int CurrentScore { get; init; }
    public int QuestionsAnswered { get; init; }
    public bool TestCompleted { get; init; }
}
