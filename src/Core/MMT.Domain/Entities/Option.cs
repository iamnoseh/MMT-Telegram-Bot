using MMT.Domain.Common;

namespace MMT.Domain.Entities;


public class Option : BaseEntity
{
    public int QuestionId { get; set; }
    public string OptionA { get; set; } = string.Empty;
    public string OptionB { get; set; } = string.Empty;
    public string OptionC { get; set; } = string.Empty;
    public string OptionD { get; set; } = string.Empty;
    public string CorrectAnswer { get; set; } = string.Empty;
    
    public Question Question { get; set; } = null!;
}
