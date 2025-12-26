using MediatR;

namespace MMT.Application.Features.Duels.Commands.CreateDuel;

public record CreateDuelCommand : IRequest<CreateDuelResult>
{
    public long ChallengerChatId { get; init; }
    public int SubjectId { get; init; }
}

public record CreateDuelResult
{
    public bool Success { get; init; }
    public string DuelCode { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}
