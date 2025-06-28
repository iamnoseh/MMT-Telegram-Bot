namespace TelegramBot.Domain.Entities;

public class Book
{
    public int Id { get; set; }
    public string Title { get; set; }
    public string Description { get; set; }
    public string FileName { get; set; }
    public string FilePath { get; set; }
    public string FileExtension { get; set; }
    public long FileSize { get; set; }
    public int CategoryId { get; set; }
    public int Year { get; set; }
    public int UploadedByUserId { get; set; }
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;
    public int DownloadCount { get; set; } = 0;
    
    // Navigation properties
    public BookCategory Category { get; set; }
    public User UploadedByUser { get; set; }
} 