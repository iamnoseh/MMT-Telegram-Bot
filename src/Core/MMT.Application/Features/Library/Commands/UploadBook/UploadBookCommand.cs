using MediatR;

namespace MMT.Application.Features.Library.Commands.UploadBook;

public record UploadBookCommand : IRequest<UploadBookResult>
{
    public long AdminChatId { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public int PublicationYear { get; init; }
    public string Category { get; init; } = "Умумӣ"; // Simple string
    public string FileName { get; init; } = string.Empty;
    public string FilePath { get; init; } = string.Empty;
}

public record UploadBookResult
{
    public bool Success { get; init; }
    public int BookId { get; init; }
    public string Message { get; init; } = string.Empty;
}
