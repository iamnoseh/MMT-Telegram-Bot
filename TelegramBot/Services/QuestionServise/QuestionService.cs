using Domain.DTOs;
using Microsoft.EntityFrameworkCore;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramBot.Domain.DTOs;
using TelegramBot.Domain.Entities;
using TelegramBot.Services.QuestionService;

namespace TelegramBot.Services.QuestionService;

public class QuestionService : IQuestionService
{
    private readonly DataContext _context;
    private readonly Random _random;

    public QuestionService(DataContext context)
    {
        _context = context;
        _random = new Random();
    }

    public async Task<List<GetQuestionDTO>> GetQuestionsBySubject(int subjectId)
    {
        return await _context.Questions
            .Where(q => q.SubjectId == subjectId)
            .Select(q => new GetQuestionDTO
            {
                Id = q.Id,
                QuestionText = q.QuestionText,
                SubjectId = q.SubjectId,
                SubjectName = q.Subject.Name
            })
            .ToListAsync();
    }

    public async Task<GetQuestionWithOptionsDTO> GetQuestionById(int id)
    {
        return await _context.Questions
            .Where(q => q.Id == id)
            .Select(q => new GetQuestionWithOptionsDTO
            {
                QuestionId = q.Id,
                QuestionText = q.QuestionText,
                FirstOption = q.Option.OptionA,
                SecondOption = q.Option.OptionB,
                ThirdOption = q.Option.OptionC,
                FourthOption = q.Option.OptionD,
                Answer = q.Option.CorrectAnswer,
                SubjectId = q.SubjectId,
                SubjectName = q.Subject.Name
            })
            .FirstOrDefaultAsync();
    }

    public async Task<GetQuestionWithOptionsDTO> GetRandomQuestionBySubject(int subjectId)
    {
        var questionsCount = await _context.Questions
            .Where(q => q.SubjectId == subjectId)
            .CountAsync();

        if (questionsCount == 0)
            return null;

        var skip = _random.Next(questionsCount);

        return await _context.Questions
            .Where(q => q.SubjectId == subjectId)
            .Include(q => q.Option)
            .Include(q => q.Subject)
            .Skip(skip)
            .Take(1)
            .Select(q => new GetQuestionWithOptionsDTO
            {
                QuestionId = q.Id,
                QuestionText = q.QuestionText,
                FirstOption = q.Option.OptionA,
                SecondOption = q.Option.OptionB,
                ThirdOption = q.Option.OptionC,
                FourthOption = q.Option.OptionD,
                Answer = q.Option.CorrectAnswer,
                SubjectId = q.SubjectId,
                SubjectName = q.Subject.Name
            })
            .FirstOrDefaultAsync();
    }

    public async Task<QuestionDTO> CreateQuestion(QuestionDTO questionDto)
    {
        var question = new Question
        {
            QuestionText = questionDto.QuestionText,
            SubjectId = questionDto.SubjectId,
            Option = new Option
            {
                OptionA = questionDto.OptionA,
                OptionB = questionDto.OptionB,
                OptionC = questionDto.OptionC,
                OptionD = questionDto.OptionD,
                CorrectAnswer = questionDto.CorrectAnswer
            }
        };

        _context.Questions.Add(question);
        await _context.SaveChangesAsync();
        return questionDto;
    }

    public async Task<QuestionDTO> UpdateQuestion(int id, QuestionDTO questionDto)
    {
        var question = await _context.Questions
            .Include(q => q.Option)
            .FirstOrDefaultAsync(q => q.Id == id);

        if (question == null)
            return null;

        question.QuestionText = questionDto.QuestionText;
        question.SubjectId = questionDto.SubjectId;
        question.Option.OptionA = questionDto.OptionA;
        question.Option.OptionB = questionDto.OptionB;
        question.Option.OptionC = questionDto.OptionC;
        question.Option.OptionD = questionDto.OptionD;
        question.Option.CorrectAnswer = questionDto.CorrectAnswer;

        await _context.SaveChangesAsync();
        return questionDto;
    }

    public async Task<bool> DeleteQuestion(int id)
    {
        var question = await _context.Questions.FindAsync(id);
        if (question == null)
            return false;

        _context.Questions.Remove(question);
        await _context.SaveChangesAsync();
        return true;
    }

    private IReplyMarkup GetMainButtons()
    {
        return new ReplyKeyboardMarkup
        {
            Keyboard = new List<List<KeyboardButton>>
            {
                new() { new KeyboardButton("📚 Интихоби фан") },
                new() { new KeyboardButton("❓ Саволи нав"), new KeyboardButton("🏆 Топ") },
                new() { new KeyboardButton("👤 Профил"), new KeyboardButton("ℹ️ Кумак") }
            },
            ResizeKeyboard = true
        };
    }
}
