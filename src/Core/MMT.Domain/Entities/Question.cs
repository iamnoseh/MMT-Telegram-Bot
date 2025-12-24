using MMT.Domain.Common;

namespace MMT.Domain.Entities;


public class Question : BaseEntity
{
    public string QuestionText { get; set; } = string.Empty;
    public int SubjectId { get; set; }
    public Subject Subject { get; set; } = null!;
    public Option Option { get; set; } = null!;
    public ICollection<UserResponse> UserResponses { get; set; } = new List<UserResponse>();
}
