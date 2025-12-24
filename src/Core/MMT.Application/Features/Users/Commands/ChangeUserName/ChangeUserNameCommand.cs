using MediatR;

namespace MMT.Application.Features.Users.Commands.ChangeUserName;

public record ChangeUserNameCommand : IRequest<ChangeUserNameResult>
{
    public long ChatId { get; init; }
    public string NewName { get; init; } = string.Empty;
}

public record ChangeUserNameResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
}
