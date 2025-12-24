using MediatR;

namespace MMT.Application.Features.Admin.Commands.BroadcastMessage;

public record BroadcastMessageCommand : IRequest<BroadcastMessageResult>
{
    public long AdminChatId { get; init; }
    public string Message { get; init; } = string.Empty;
}

public record BroadcastMessageResult
{
    public bool Success { get; init; }
    public int TotalUsers { get; init; }
    public int SuccessCount { get; init; }
    public int FailureCount { get; init; }
    public string Message { get; init; } = string.Empty;
}
