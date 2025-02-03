namespace TelegramBot.Domain.Entities;

public class UserResponse
{
    public int Id { get; set; }
    public long ChatId { get; set; }
    public int QuestionId { get; set; }
    public string SelectedOption { get; set; }
    public bool IsCorrect { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}