using TelegramBot.Domain.DTOs;

namespace Domain.DTOs;



public class GetQuestionWithOptionsDTO
{
    public int QuestionId { get; set; }
    public string QuestionText { get; set; }
    public string FirstOption { get; set; }  // OptionA
    public string SecondOption { get; set; } // OptionB
    public string ThirdOption { get; set; }  // OptionC
    public string FourthOption { get; set; } // OptionD
    public string Answer { get; set; }       // CorrectAnswer
    public int SubjectId { get; set; }
    public string SubjectName { get; set; }
}