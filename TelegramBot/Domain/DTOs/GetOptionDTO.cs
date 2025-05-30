namespace TelegramBot.Domain.DTOs;

public class GetOptionDTO
{
    public int Id { get; set; }
    public int QuestionId { get; set; }
    public string OptionA { get; set; }
    public string OptionB { get; set; }
    public string OptionC { get; set; }
    public string OptionD { get; set; }
}
