using MMT.Domain.Common;

namespace MMT.Domain.Entities;


public class DuelGame : BaseEntity
{
    public long Player1ChatId { get; set; }
    public long Player2ChatId { get; set; }
    public int SubjectId { get; set; }
    public int Player1Score { get; set; }
    public int Player2Score { get; set; }
    public int CurrentRound { get; set; } = 1;
    public bool IsFinished { get; set; }
    public DateTime? FinishedAt { get; set; }
    
    public Subject Subject { get; set; } = null!;
    
    public void FinishGame(long winnerChatId)
    {
        IsFinished = true;
        FinishedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }
    
    public void UpdateScore(long playerChatId, int points)
    {
        if (playerChatId == Player1ChatId)
            Player1Score += points;
        else if (playerChatId == Player2ChatId)
            Player2Score += points;
            
        UpdatedAt = DateTime.UtcNow;
    }
    
    public long? GetWinner()
    {
        if (!IsFinished) return null;
        
        if (Player1Score > Player2Score) return Player1ChatId;
        if (Player2Score > Player1Score) return Player2ChatId;
        return null; 
    }
}
