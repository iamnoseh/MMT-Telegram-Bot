using System;
using System.Collections.Generic;

namespace TelegramBot.Domain.Entities;

public class DuelGame
{
    public int Id { get; set; }
    public long Player1ChatId { get; set; }
    public long Player2ChatId { get; set; }
    public int SubjectId { get; set; }
    public int Player1Score { get; set; }
    public int Player2Score { get; set; }
    public int CurrentRound { get; set; } = 1;
    public bool IsFinished { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? FinishedAt { get; set; }
    
    // Navigation property
    public required Subject Subject { get; set; }
}
