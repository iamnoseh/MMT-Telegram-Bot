using MediatR;
using MMT.Application.Common.Interfaces.Repositories;

namespace MMT.Application.Features.Tests.Commands.StartTestSession;

public record StartTestSessionCommand : IRequest<StartTestSessionResult>
{
    public long ChatId { get; init; }
    public int SubjectId { get; init; }
}

public record StartTestSessionResult
{
    public bool Success { get; init; }
    public int SessionId { get; init; }
    public QuestionDto? FirstQuestion { get; init; }
    public string Message { get; init; } = string.Empty;
}

public record QuestionDto
{
    public int Id { get; init; }
    public string Text { get; init; } = string.Empty;
    public string OptionA { get; init; } = string.Empty;
    public string OptionB { get; init; } = string.Empty;
    public string OptionC { get; init; } = string.Empty;
    public string OptionD { get; init; } = string.Empty;
    public int SubjectId { get; init; }
    public string SubjectName { get; init; } = string.Empty;
}
