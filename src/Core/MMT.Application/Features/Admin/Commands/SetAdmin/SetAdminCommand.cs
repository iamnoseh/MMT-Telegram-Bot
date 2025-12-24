using MediatR;

namespace MMT.Application.Features.Admin.Commands.SetAdmin;

public record SetAdminCommand : IRequest<SetAdminResult>
{
    public long AdminChatId { get; init; }
    public string? TargetUsername { get; init; }
    public string? TargetPhoneNumber { get; init; }
    public bool MakeAdmin { get; init; } = true;
}

public record SetAdminResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
}
