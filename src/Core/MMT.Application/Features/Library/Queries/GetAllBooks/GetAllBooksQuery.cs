using MediatR;

namespace MMT.Application.Features.Library.Queries.GetAllBooks;

public record GetAllBooksQuery : IRequest<List<BookDto>>
{
    public int? CategoryId { get; init; }
}

public record BookDto
{
    public int Id { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public int PublicationYear { get; init; }
    public string CategoryName { get; init; } = string.Empty;
    public string UploadedBy { get; init; } = string.Empty;
    public DateTime UploadedAt { get; init; }
}
