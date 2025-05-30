namespace TelegramBot.Domain.DTOs;

public class GetQuestionDTO
{
    public int Id { get; set; }
    public string QuestionText { get; set; }
    public int SubjectId { get; set; }
    public string SubjectName { get; set; }
}
