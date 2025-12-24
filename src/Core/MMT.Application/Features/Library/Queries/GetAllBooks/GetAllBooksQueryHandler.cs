using MediatR;
using Microsoft.Extensions.Logging;
using MMT.Application.Common.Interfaces.Repositories;

namespace MMT.Application.Features.Library.Queries.GetAllBooks;

public class GetAllBooksQueryHandler(
    IUnitOfWork unitOfWork,
    ILogger<GetAllBooksQueryHandler> logger)
    : IRequestHandler<GetAllBooksQuery, List<BookDto>>
{
    public async Task<List<BookDto>> Handle(GetAllBooksQuery request, CancellationToken ct)
    {
        var books = await unitOfWork.Books.GetAllAsync(ct);
        
        if (request.CategoryId.HasValue)
        {
            books = books.Where(b => b.CategoryId == request.CategoryId.Value).ToList();
        }
        
        var bookDtos = books.Select(b => new BookDto
        {
            Id = b.Id,
            Title = b.Title,
            Description = b.Description,
            FileName = b.FileName,
            PublicationYear = b.Year,
            CategoryName = b.Category.Name,
            UploadedBy = b.UploadedByUser.Name,
            UploadedAt = b.CreatedAt
        }).ToList();
        
        logger.LogInformation("Retrieved {Count} books", bookDtos.Count);
        
        return bookDtos;
    }
}
