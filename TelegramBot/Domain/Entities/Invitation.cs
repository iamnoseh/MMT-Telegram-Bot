namespace TelegramBot.Domain.Entities;

public class Invitation
{
    public int Id { get; set; }
    public long InviterChatId { get; set; }
    public long InviteeChatId { get; set; }
    public string Status { get; set; } = "pending"; // "pending" or "accepted"
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? AcceptedAt { get; set; }
}