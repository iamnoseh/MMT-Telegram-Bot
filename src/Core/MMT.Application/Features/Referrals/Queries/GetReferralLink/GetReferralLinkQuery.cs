using MediatR;

namespace MMT.Application.Features.Referrals.Queries.GetReferralLink;

public record GetReferralLinkQuery : IRequest<ReferralLinkResult>
{
    public long ChatId { get; init; }
    public string BotUsername { get; init; } = string.Empty;
}

public record ReferralLinkResult
{
    public string ReferralCode { get; init; } = string.Empty;
    public string ReferralLink { get; init; } = string.Empty;
    public int TotalReferrals { get; init; }
}
