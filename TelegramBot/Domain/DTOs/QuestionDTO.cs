using TelegramBot.Domain.Entities;

namespace TelegramBot.Domain.DTOs;

public class QuestionDTO
{
    public string QuestionText { get; set; }
    public int OptionDTOId { get; set; }
    public List<Option> Options { get; set; }
}

