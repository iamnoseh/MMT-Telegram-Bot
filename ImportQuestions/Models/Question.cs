

namespace TelegramBot.Domain.Entities;

public class Question
{
    public int QuestionId { get; set; }
    public string QuestionText { get; set; }
   
   
   //navigation
     public int OptionId { get; set; }
    public Option Option { get; set; }
}

