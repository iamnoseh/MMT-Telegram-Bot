using System.Collections.Generic;

namespace TelegramBot.Domain.DTOs;

public class GetSubjectDTO
{
    public int Id { get; set; }
    public string Name { get; set; }
    public int QuestionCount { get; set; }
}