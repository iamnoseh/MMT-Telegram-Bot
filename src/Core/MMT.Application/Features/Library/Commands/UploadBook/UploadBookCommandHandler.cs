using MediatR;
using Microsoft.Extensions.Logging;
using MMT.Application.Common.Interfaces.Repositories;
using MMT.Domain.Entities;

namespace MMT.Application.Features.Library.Commands.UploadBook;

public class UploadBookCommandHandler(
    IUnitOfWork unitOfWork,
    ILogger<UploadBookCommandHandler> logger)
    : IRequestHandler<UploadBookCommand, UploadBookResult>
{
    public async Task<UploadBookResult> Handle(UploadBookCommand request, CancellationToken ct)
    {
        var admin = await unitOfWork.Users.GetByChatIdAsync(request.AdminChatId, ct);
        if (admin == null || !admin.IsAdmin)
        {
            logger.LogWarning("Unauthorized book upload attempt by {ChatId}", request.AdminChatId);
            return new UploadBookResult
            {
                Success = false,
                Message = "Шумо ҳуқуқи боргузорӣ надоред."
            };
        }
        
        var book = new Book
        {
            Title = request.Title,
            Description = request.Description,
            Year = request.PublicationYear,
            Category = request.Category,
            FileName = request.FileName,
            FilePath = request.FilePath,
            UploadedByUserId = admin.Id,
            IsActive = true
        };
        
        await unitOfWork.Books.AddAsync(book, ct);
        await unitOfWork.SaveChangesAsync(ct);
        
        logger.LogInformation("Book uploaded: {Title} by admin {AdminChatId}", request.Title, request.AdminChatId);
        
        return new UploadBookResult
        {
            Success = true,
            BookId = book.Id,
            Message = $"Китоб '{request.Title}' муваффақият боргузорӣ шуд!"
        };
    }
}
