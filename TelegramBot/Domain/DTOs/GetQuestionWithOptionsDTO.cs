using TelegramBot.Domain.DTOs;

namespace Domain.DTOs;



public class GetQuestionWithOptionsDTO
{
    public int QuestionId { get; set; }
    public string QuestionText { get; set; }
    public string Answer { get; set; }
    public string FirstOption { get; set; }
    public string SecondOption { get; set; }
    public string ThirdOption { get; set; }
    public string FourthOption { get; set; }
}