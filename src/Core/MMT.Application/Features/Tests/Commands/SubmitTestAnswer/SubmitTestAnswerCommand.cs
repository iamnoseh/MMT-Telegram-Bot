using MediatR;
using MMT.Application.Features.Tests.Commands.StartTestSession;

namespace MMT.Application.Features.Tests.Commands.SubmitTestAnswer;

public record SubmitTestAnswerCommand : IRequest<SubmitTestAnswerResult>
{
    public long ChatId { get; init; }
    public int QuestionId { get; init; }
    public string SelectedOption { get; init; } = string.Empty;
}

public record SubmitTestAnswerResult
{
    public bool Success { get; init; }
    public bool IsCorrect { get; init; }
    public int NewScore { get; init; }
    public int QuestionNumber { get; init; }
    public bool TestCompleted { get; init; }
    public QuestionDto? NextQuestion { get; init; }
    public string Message { get; init; } = string.Empty;
}
