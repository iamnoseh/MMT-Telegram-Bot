using MMT.Domain.Common;

namespace MMT.Domain.Entities;

public class UserState : BaseEntity
{
    public long ChatId { get; set; }
    public int? SelectedSubjectId { get; set; }
    public int? CurrentSubjectId { get; set; }
    
    public int TestScore { get; set; }
    public int TestQuestionsCount { get; set; }
    
    public bool IsPendingBroadcast { get; set; }
    public bool IsPendingNameChange { get; set; }
    
    public BookUploadStep? BookUploadStep { get; set; }
    public string? BookTitle { get; set; }
    public string? BookDescription { get; set; }
    public int? BookYear { get; set; }
    public int? BookCategoryId { get; set; }
    
    public DateTime LastActivityAt { get; set; } = DateTime.UtcNow;
    
    public Subject? SelectedSubject { get; set; }
    
    public void SelectSubject(int subjectId)
    {
        SelectedSubjectId = subjectId;
        UpdatedAt = DateTime.UtcNow;
    }
    
    public void ClearBookUpload()
    {
        BookUploadStep = null;
        BookTitle = null;
        BookDescription = null;
        BookYear = null;
        BookCategoryId = null;
        UpdatedAt = DateTime.UtcNow;
    }
    
    public void UpdateActivity()
    {
        LastActivityAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }
}

public enum BookUploadStep
{
    Title = 1,
    Description = 2,
    Year = 3,
    File = 4
}
