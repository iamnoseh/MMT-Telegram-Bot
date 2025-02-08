namespace TelegramBot.Domain.Entities
{
public class Option
{
    public int OptionId { get; set; }
    public string FirstVariant { get; set; }
    public string SecondVariant { get; set; }
    public string ThirdVariant { get; set; }
    public string FourthVariant { get; set; }
    public string Answer { get; set; }
    
    public Question Question { get; set; }
}
}