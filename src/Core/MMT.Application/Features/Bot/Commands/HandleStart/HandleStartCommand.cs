using MediatR;

namespace MMT.Application.Features.Bot.Commands.HandleStart;

public record HandleStartCommand : IRequest<HandleStartResult>
{
    public long ChatId { get; init; }
    public string? Username { get; init; }
    public string? FirstName { get; init; }
}

public record HandleStartResult
{
    public bool IsRegistered { get; init; }
    public string Message { get; init; } = string.Empty;
    public bool ShouldRequestPhone { get; init; }
}
