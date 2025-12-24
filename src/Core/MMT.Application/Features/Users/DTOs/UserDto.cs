namespace MMT.Application.Features.Users.DTOs;

public record UserDto
{
    public int Id { get; init; }
    public long ChatId { get; init; }
    public string Username { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string City { get; init; } = string.Empty;
    public int Score { get; init; }
    public bool IsAdmin { get; init; }
}
