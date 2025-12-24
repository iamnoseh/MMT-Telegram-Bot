using MediatR;

namespace MMT.Application.Features.Users.Queries.GetUserProfile;

public record GetUserProfileQuery : IRequest<UserProfileDto?>
{
    public long ChatId { get; init; }
}

public record UserProfileDto
{
    public string Name { get; init; } = string.Empty;
    public string City { get; init; } = string.Empty;
    public int Score { get; init; }
    public int Level { get; init; }
    public int Rank { get; init; }
    public string PhoneNumber { get; init; } = string.Empty;
    public bool HasChangedName { get; init; }
}
