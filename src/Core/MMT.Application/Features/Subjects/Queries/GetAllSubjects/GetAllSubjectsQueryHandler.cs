using MediatR;
using MMT.Application.Common.Interfaces.Repositories;

namespace MMT.Application.Features.Subjects.Queries.GetAllSubjects;

public class GetAllSubjectsQueryHandler(IUnitOfWork unitOfWork)
    : IRequestHandler<GetAllSubjectsQuery, List<SubjectDto>>
{
    public async Task<List<SubjectDto>> Handle(GetAllSubjectsQuery request, CancellationToken ct)
    {
        var subjects = await unitOfWork.Subjects.GetAllAsync(ct);
        
        return subjects.Select(s => new SubjectDto
        {
            Id = s.Id,
            Name = s.Name
        }).ToList();
    }
}
