using Domain.DTOs;
using TelegramBot.Domain.DTOs;
using TelegramBot.Domain.Entities;

namespace TelegramBot.Services.QuestionServise;

public interface IQuestionService
{
    Task<GetQuestionDTO> GetQuestionAsync(int requestId);
    Task<GetOptionDTO> GetOptionDTOAsync(int questionId);
     Task<List<GetOptionDTO>> GetOptionsAsyncs();
     Task<GetQuestionWithOptionsDTO> GetQuestionWithOptionsDTO();
     Task<GetQuestionWithOptionsDTO?> GetQuestionById(int questionId);
}

