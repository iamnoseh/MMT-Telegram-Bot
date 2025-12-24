using MMT.Domain.Common;

namespace MMT.Domain.Entities;

public class RegistrationSession : BaseEntity
{
    public long ChatId { get; set; }
    public string? Username { get; set; }
    public string? PhoneNumber { get; set; }
    public string? Name { get; set; }
    public string? City { get; set; }
    public RegistrationStep CurrentStep { get; set; } = RegistrationStep.Phone;
    public bool IsCompleted { get; set; }
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    
    public void MoveToNextStep()
    {
        CurrentStep = CurrentStep switch
        {
            RegistrationStep.Phone => RegistrationStep.Name,
            RegistrationStep.Name => RegistrationStep.City,
            RegistrationStep.City => RegistrationStep.Completed,
            _ => CurrentStep
        };
        
        if (CurrentStep == RegistrationStep.Completed)
        {
            IsCompleted = true;
            CompletedAt = DateTime.UtcNow;
        }
        
        UpdatedAt = DateTime.UtcNow;
    }
    
    public bool IsValidForCompletion()
    {
        return !string.IsNullOrEmpty(PhoneNumber) 
            && !string.IsNullOrEmpty(Name) 
            && !string.IsNullOrEmpty(City);
    }
}

public enum RegistrationStep
{
    Phone = 0,
    Name = 1,
    City = 2,
    Completed = 3
}
