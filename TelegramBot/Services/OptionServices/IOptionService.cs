using TelegramBot.Domain.DTOs;
using TelegramBot.Domain.Entities;

namespace TelegramBot.Services.OptionServices;

public interface IOptionService
{
    Task<GetOptionDTO> GetOptionByQuestionId(int questionId);
    Task<GetOptionDTO> CreateOption(int questionId, Option option);
    Task<GetOptionDTO> UpdateOption(int questionId, Option option);
    Task<bool> DeleteOption(int questionId);
}
