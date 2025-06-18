namespace TelegramBot.Domain.Entities;

public class User
{
    public int Id { get; set; }
    public long ChatId { get; set; }
    public string Username { get; set; }
    public string Name { get; set; }
    public string PhoneNumber { get; set; }
    public string City { get; set; }
    public int Score { get; set; }
    public bool IsLeft { get; set; } = false;
    public bool IsAdmin { get; set; } = false;
    public bool HasChangedName { get; set; } = false; // Track if user has changed name once
    // Алоқа бо ҷавобҳои корбар
    public List<UserResponse> UserResponses { get; set; } = new();

}
