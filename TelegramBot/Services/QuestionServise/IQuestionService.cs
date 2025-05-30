using Domain.DTOs;
using TelegramBot.Domain.DTOs;
using TelegramBot.Domain.Entities;

namespace TelegramBot.Services.QuestionService;

public interface IQuestionService
{
    Task<List<GetQuestionDTO>> GetQuestionsBySubject(int subjectId);
    Task<GetQuestionWithOptionsDTO> GetQuestionById(int id);
    Task<GetQuestionWithOptionsDTO> GetRandomQuestionBySubject(int subjectId);
    Task<QuestionDTO> CreateQuestion(QuestionDTO questionDto);
    Task<QuestionDTO> UpdateQuestion(int id, QuestionDTO questionDto);
    Task<bool> DeleteQuestion(int id);
}

