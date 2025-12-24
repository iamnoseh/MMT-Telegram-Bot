using MediatR;

namespace MMT.Application.Features.Bot.Commands.HandlePhoneRegistration;

public record HandlePhoneRegistrationCommand : IRequest<HandlePhoneRegistrationResult>
{
    public long ChatId { get; init; }
    public string PhoneNumber { get; init; } = string.Empty;
    public string? Username { get; init; }
    public string? FirstName { get; init; }
}

public record HandlePhoneRegistrationResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public RegistrationStep NextStep { get; init; }
}

public enum RegistrationStep
{
    Phone,
    Name,
    City,
    Completed
}
