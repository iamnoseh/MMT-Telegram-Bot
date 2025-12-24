using MediatR;

namespace MMT.Application.Features.Users.Commands.UpdateUserScore;

public record UpdateUserScoreCommand : IRequest<bool>
{
    public long ChatId { get; init; }
    public int Points { get; init; }
}
