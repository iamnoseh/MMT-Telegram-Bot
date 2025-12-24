using MMT.Domain.Common;

namespace MMT.Domain.Entities;


public class Invitation : BaseEntity
{
    public long InviterChatId { get; set; }
    public long InviteeChatId { get; set; }
    public string Status { get; set; } = "pending"; 
    public DateTime? AcceptedAt { get; set; }
    
    public void Accept()
    {
        Status = "accepted";
        AcceptedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }
    
    public bool IsPending() => Status == "pending";
    public bool IsAccepted() => Status == "accepted";
}
