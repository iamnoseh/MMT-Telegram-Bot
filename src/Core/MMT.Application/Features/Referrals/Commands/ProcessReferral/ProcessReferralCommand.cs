using MediatR;

namespace MMT.Application.Features.Referrals.Commands.ProcessReferral;

public record ProcessReferralCommand : IRequest<ProcessReferralResult>
{
    public long NewUserChatId { get; init; }
    public string ReferralCode { get; init; } = string.Empty;
}

public record ProcessReferralResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public string? ReferrerName { get; init; }
}
