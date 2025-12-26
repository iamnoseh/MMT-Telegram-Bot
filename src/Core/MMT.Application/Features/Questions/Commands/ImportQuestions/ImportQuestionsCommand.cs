using MediatR;
using MMT.Application.Common.DTOs;

namespace MMT.Application.Features.Questions.Commands.ImportQuestions;

public record ImportQuestionsCommand : IRequest<QuestionImportResult>
{
    public int SubjectId { get; init; }
    public byte[] FileContent { get; init; } = Array.Empty<byte>();
    public string FileName { get; init; } = string.Empty;
    public string FileExtension { get; init; } = string.Empty;
}
