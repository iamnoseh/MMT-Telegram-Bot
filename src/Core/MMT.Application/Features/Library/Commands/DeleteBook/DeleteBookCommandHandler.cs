using MediatR;
using Microsoft.Extensions.Logging;
using MMT.Application.Common.Interfaces.Repositories;

namespace MMT.Application.Features.Library.Commands.DeleteBook;

public class DeleteBookCommandHandler(
    IUnitOfWork unitOfWork,
    ILogger<DeleteBookCommandHandler> logger)
    : IRequestHandler<DeleteBookCommand, DeleteBookResult>
{
    public async Task<DeleteBookResult> Handle(DeleteBookCommand request, CancellationToken ct)
    {
        var admin = await unitOfWork.Users.GetByChatIdAsync(request.AdminChatId, ct);
        if (admin == null || !admin.IsAdmin)
        {
            logger.LogWarning("Unauthorized book delete attempt by {ChatId}", request.AdminChatId);
            return new DeleteBookResult
            {
                Success = false,
                Message = "Шумо ҳуқуқи нобуд кардани китоб надоред."
            };
        }
        
        var book = await unitOfWork.Books.GetByIdAsync(request.BookId, ct);
        if (book == null)
        {
            return new DeleteBookResult
            {
                Success = false,
                Message = "Китоб ёфт нашуд."
            };
        }
        
        // Soft delete
        book.IsActive = false;
        book.IsDeleted = true;
        unitOfWork.Books.Update(book);
        await unitOfWork.SaveChangesAsync(ct);
        
        logger.LogInformation("Book {BookId} ({Title}) deleted by admin {AdminChatId}", 
            request.BookId, book.Title, request.AdminChatId);
        
        return new DeleteBookResult
        {
            Success = true,
            Message = $"Китоб '{book.Title}' нобуд карда шуд."
        };
    }
}
