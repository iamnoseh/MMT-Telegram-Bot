namespace MMT.Application.Common.DTOs;

public class QuestionImportResult
{
    public int TotalParsed { get; set; }
    public int SuccessfullyAdded { get; set; }
    public int Duplicates { get; set; }
    public int Errors { get; set; }
    public List<string> ErrorMessages { get; set; } = new();
}

public class ParsedQuestion
{
    public string QuestionText { get; set; } = string.Empty;
    public string OptionA { get; set; } = string.Empty;
    public string OptionB { get; set; } = string.Empty;
    public string OptionC { get; set; } = string.Empty;
    public string OptionD { get; set; } = string.Empty;
    public string CorrectAnswer { get; set; } = string.Empty; // A, B, C, or D
}
