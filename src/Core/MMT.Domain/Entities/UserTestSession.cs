using MMT.Domain.Common;

namespace MMT.Domain.Entities;

public class UserTestSession : BaseEntity
{
    public long ChatId { get; set; }
    public int SubjectId { get; set; }
    public int CurrentQuestionNumber { get; set; }
    public int CurrentQuestionId { get; set; }
    public int Score { get; set; }
    public bool IsActive { get; set; }
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public DateTime? LastQuestionSentAt { get; set; }
    
    public Subject Subject { get; set; } = null!;
    public Question CurrentQuestion { get; set; } = null!;
    
    public void CompleteSession()
    {
        IsActive = false;
        CompletedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }
    
    public void AnswerQuestion(bool isCorrect, int points = 1)
    {
        if (isCorrect)
            Score += points;
        
        CurrentQuestionNumber++;
        UpdatedAt = DateTime.UtcNow;
    }
}
