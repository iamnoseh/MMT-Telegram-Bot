using MediatR;

namespace MMT.Application.Features.Bot.Commands.HandleCityRegistration;

public record HandleCityRegistrationCommand : IRequest<HandleCityRegistrationResult>
{
    public long ChatId { get; init; }
    public string City { get; init; } = string.Empty;
}

public record HandleCityRegistrationResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public bool IsCompleted { get; init; }
}
