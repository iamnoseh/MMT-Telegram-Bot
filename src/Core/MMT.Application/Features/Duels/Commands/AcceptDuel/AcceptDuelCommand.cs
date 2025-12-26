using MediatR;

namespace MMT.Application.Features.Duels.Commands.AcceptDuel;

public record AcceptDuelCommand : IRequest<AcceptDuelResult>
{
    public int DuelId { get; init; }
    public long OpponentChatId { get; init; }
}

public record AcceptDuelResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public int SubjectId { get; init; }
}
