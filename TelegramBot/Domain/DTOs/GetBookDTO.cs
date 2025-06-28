namespace TelegramBot.Domain.DTOs;

public class GetBookDTO
{
    public int Id { get; set; }
    public string Title { get; set; }
    public string Description { get; set; }
    public string FileName { get; set; }
    public string FileExtension { get; set; }
    public long FileSize { get; set; }
    public DateTime UploadedAt { get; set; }
    public int DownloadCount { get; set; }
    public string CategoryName { get; set; }
    public string Cluster { get; set; }
    public int Year { get; set; }
    public string UploadedByUserName { get; set; }
    public string FilePath { get; set; } // Add FilePath property
    public string Name   // Add Name property that returns Title
    {
        get { return Title; }
    }
}
