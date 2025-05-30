namespace TelegramBot.Domain.Entities;


public class Option
{
    public int Id { get; set; }
    public int QuestionId { get; set; }
    public string OptionA { get; set; }
    public string OptionB { get; set; }
    public string OptionC { get; set; }
    public string OptionD { get; set; }
    public string CorrectAnswer { get; set; }
    // Алоқа бо савол
    public Question Question { get; set; }
}
