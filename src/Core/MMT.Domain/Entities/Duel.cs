namespace MMT.Domain.Entities;

public class Duel
{
    public int Id { get; set; }
    public int ChallengerId { get; set; }
    public User Challenger { get; set; } = null!;
    public int? OpponentId { get; set; }
    public User? Opponent { get; set; }
    public int SubjectId { get; set; }
    public Subject Subject { get; set; } = null!;
    public string DuelCode { get; set; } = string.Empty;
    public DuelStatus Status { get; set; }
    public int QuestionCount { get; set; } = 10;
    public int ChallengerScore { get; set; } = 0;
    public int OpponentScore { get; set; } = 0;
    public int? WinnerId { get; set; }
    public User? Winner { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    
    public ICollection<DuelAnswer> Answers { get; set; } = new List<DuelAnswer>();
}

public enum DuelStatus
{
    Pending,    
    Active,     
    Completed, 
    Cancelled   
}
