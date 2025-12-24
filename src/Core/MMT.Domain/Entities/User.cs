using MMT.Domain.Common;

namespace MMT.Domain.Entities;

public class User : BaseEntity
{
    public long ChatId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public int Score { get; set; }
    public bool IsLeft { get; set; } = false;
    public bool IsAdmin { get; set; } = false;
    public bool HasChangedName { get; set; } = false;
    
    public ICollection<UserResponse> UserResponses { get; set; } = new List<UserResponse>();
    public ICollection<Book> UploadedBooks { get; set; } = new List<Book>();
    
    public void UpdateScore(int points)
    {
        Score += points;
        UpdatedAt = DateTime.UtcNow;
    }
    
    public bool CanChangeName() => !HasChangedName;
    
    public void ChangeName(string newName)
    {
        if (!CanChangeName())
            throw new InvalidOperationException("Шумо аллакай номро иваз кардаед");
            
        Name = newName;
        HasChangedName = true;
        UpdatedAt = DateTime.UtcNow;
    }
    
    public void MarkAsLeft()
    {
        IsLeft = true;
        UpdatedAt = DateTime.UtcNow;
    }
}
