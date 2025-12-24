using MediatR;

namespace MMT.Application.Features.Bot.Commands.HandleNameRegistration;

public record HandleNameRegistrationCommand : IRequest<HandleNameRegistrationResult>
{
    public long ChatId { get; init; }
    public string Name { get; init; } = string.Empty;
}

public record HandleNameRegistrationResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
}
