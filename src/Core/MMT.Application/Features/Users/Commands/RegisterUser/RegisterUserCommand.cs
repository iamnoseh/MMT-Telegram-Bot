using MediatR;

namespace MMT.Application.Features.Users.Commands.RegisterUser;

public record RegisterUserCommand : IRequest<RegisterUserResult>
{
    public long ChatId { get; init; }
    public string Username { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string PhoneNumber { get; init; } = string.Empty;
    public string City { get; init; } = string.Empty;
}

public record RegisterUserResult
{
    public bool Success { get; init; }
    public int UserId { get; init; }
    public string Message { get; init; } = string.Empty;
}
