using MMT.Domain.Common;

namespace MMT.Domain.Entities;

public class Book : BaseEntity
{
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string FileExtension { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public int CategoryId { get; set; }
    public int Year { get; set; }
    public int UploadedByUserId { get; set; }
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;
    public int DownloadCount { get; set; } = 0;
    
    public BookCategory Category { get; set; } = null!;
    public User UploadedByUser { get; set; } = null!;
    
    public void IncrementDownloadCount()
    {
        DownloadCount++;
        UpdatedAt = DateTime.UtcNow;
    }
    
    public void Deactivate()
    {
        IsActive = false;
        UpdatedAt = DateTime.UtcNow;
    }
}
