using MediatR;

namespace MMT.Application.Features.Subjects.Queries.GetAllSubjects;

public record GetAllSubjectsQuery : IRequest<List<SubjectDto>>;

public record SubjectDto
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
}
