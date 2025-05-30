namespace TelegramBot.Domain.Entities;

public class Question
{
    public int Id { get; set; }
    public string QuestionText { get; set; }
    // Алоқа бо фан
    public int SubjectId { get; set; }
    public Subject Subject { get; set; }
    // Алоқа бо вариантҳои ҷавоб
    public Option Option { get; set; }
    // Алоқа бо ҷавобҳои корбарон
    public List<UserResponse> UserResponses { get; set; } = new();
}

