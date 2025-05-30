using TelegramBot.Domain.Entities;

namespace TelegramBot.Domain.DTOs;

public class QuestionDTO
{
    public int Id { get; set; }
    public string QuestionText { get; set; }
    public int SubjectId { get; set; }
    // Вариантҳои ҷавоб
    public string OptionA { get; set; }
    public string OptionB { get; set; }
    public string OptionC { get; set; }
    public string OptionD { get; set; }
    public string CorrectAnswer { get; set; }
}

