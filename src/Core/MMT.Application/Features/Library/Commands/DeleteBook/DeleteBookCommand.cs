using MediatR;

namespace MMT.Application.Features.Library.Commands.DeleteBook;

public record DeleteBookCommand : IRequest<DeleteBookResult>
{
    public long AdminChatId { get; init; }
    public int BookId { get; init; }
}

public record DeleteBookResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
}
