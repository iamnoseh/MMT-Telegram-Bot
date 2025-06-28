namespace TelegramBot.Domain.Entities;

public class BookCategory
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string Cluster { get; set; }
    public int Year { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;
    
    // Navigation properties
    public List<Book> Books { get; set; } = new();
} 