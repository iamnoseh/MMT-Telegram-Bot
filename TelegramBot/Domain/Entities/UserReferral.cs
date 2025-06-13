using System;

namespace TelegramBot.Domain.Entities;

public class UserReferral
{
    public int Id { get; set; }
    public long ReferrerChatId { get; set; }
    public long ReferredChatId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsRewarded { get; set; }
    
    // Navigation properties
    public User Referrer { get; set; }
    public User Referred { get; set; }
}
