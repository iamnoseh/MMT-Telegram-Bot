using MediatR;

namespace MMT.Application.Features.Bot.Commands.SelectSubject;

public record SelectSubjectCommand : IRequest<SelectSubjectResult>
{
    public long ChatId { get; init; }
    public int SubjectId { get; init; }
}

public record SelectSubjectResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public string SubjectName { get; init; } = string.Empty;
}
