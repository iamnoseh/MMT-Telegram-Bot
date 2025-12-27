using MMT.Domain.Common;

namespace MMT.Domain.Entities;


public class Subject : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public bool HasTimer { get; set; } = true;
    public int? TimerSeconds { get; set; } = 20;

    public ICollection<Question> Questions { get; set; } = new List<Question>();
}
