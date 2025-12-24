using MMT.Domain.Common;

namespace MMT.Domain.Entities;


public class UserResponse : BaseEntity
{
    public long ChatId { get; set; }
    public int QuestionId { get; set; }
    public string SelectedOption { get; set; } = string.Empty;
    public bool IsCorrect { get; set; }

    public Question Question { get; set; } = null!;
}
