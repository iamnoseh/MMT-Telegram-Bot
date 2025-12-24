using MediatR;
using MMT.Application.Common.Interfaces.Repositories;

namespace MMT.Application.Features.Questions.Queries.GetRandomQuestion;

public class GetRandomQuestionQueryHandler(IUnitOfWork unitOfWork)
    : IRequestHandler<GetRandomQuestionQuery, QuestionDto?>
{
    public async Task<QuestionDto?> Handle(GetRandomQuestionQuery request, CancellationToken ct)
    {
        var question = await unitOfWork.Questions.GetRandomBySubjectAsync(request.SubjectId, ct);
        
        if (question == null || question.Option == null)
            return null;
        
        var subject = await unitOfWork.Subjects.GetByIdAsync(question.SubjectId, ct);
        
        return new QuestionDto
        {
            Id = question.Id,
            Text = question.QuestionText,
            SubjectName = subject?.Name ?? "",
            OptionA = question.Option.OptionA,
            OptionB = question.Option.OptionB,
            OptionC = question.Option.OptionC,
            OptionD = question.Option.OptionD,
            CorrectAnswer = question.Option.CorrectAnswer
        };
    }
}
