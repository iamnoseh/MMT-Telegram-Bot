using System.Collections.Generic;
using Domain.DTOs;

namespace TelegramBot.Domain.DTOs;

public class SubjectWithQuestionsDTO
{
    public int Id { get; set; }
    public string Name { get; set; }
    public List<GetQuestionWithOptionsDTO> Questions { get; set; } = new();
}