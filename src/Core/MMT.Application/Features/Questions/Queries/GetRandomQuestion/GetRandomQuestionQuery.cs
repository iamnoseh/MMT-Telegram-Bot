using MediatR;
using MMT.Domain.Entities;

namespace MMT.Application.Features.Questions.Queries.GetRandomQuestion;

public record GetRandomQuestionQuery : IRequest<QuestionDto?>
{
    public int SubjectId { get; init; }
}

public record QuestionDto
{
    public int Id { get; init; }
    public string Text { get; init; } = string.Empty;
    public string SubjectName { get; init; } = string.Empty;
    public string OptionA { get; init; } = string.Empty;
    public string OptionB { get; init; } = string.Empty;
    public string OptionC { get; init; } = string.Empty;
    public string OptionD { get; init; } = string.Empty;
    public string CorrectAnswer { get; init; } = string.Empty;
}
