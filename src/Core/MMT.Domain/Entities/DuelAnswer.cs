namespace MMT.Domain.Entities;

public class DuelAnswer
{
    public int Id { get; set; }
    public int DuelId { get; set; }
    public Duel Duel { get; set; } = null!;
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public int QuestionId { get; set; }
    public Question Question { get; set; } = null!;
    public string SelectedAnswer { get; set; } = string.Empty;
    public bool IsCorrect { get; set; }
    public DateTime AnsweredAt { get; set; }
    public DateTime? AnswerTime { get; set; }
    public int Points { get; set; } = 0;
    public TimeSpan? TimeTaken { get; set; }
}
