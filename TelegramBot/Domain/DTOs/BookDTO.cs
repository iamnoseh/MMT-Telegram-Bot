namespace TelegramBot.Domain.DTOs;

public class CreateBookDTO
{
    public string Title { get; set; }
    public string Description { get; set; }
    public int CategoryId { get; set; }
    public string FileName { get; set; }
    public string FileExtension { get; set; }
    public long FileSize { get; set; }
    public int UploadedByUserId { get; set; }
}

public class CreateBookCategoryDTO
{
    public string Name { get; set; }
    public string Cluster { get; set; }
    public int Year { get; set; }
}

public class GetBookCategoryDTO
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string Cluster { get; set; }
    public int Year { get; set; }
    public int BookCount { get; set; }
} 