using System;

namespace TelegramBot.Domain.Entities;

public class Question2Admin
{
    public int Id { get; set; }
    public long UserChatId { get; set; }
    public string QuestionText { get; set; }
    public string? Answer { get; set; }
    public bool IsAnswered { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? AnsweredAt { get; set; }
}
