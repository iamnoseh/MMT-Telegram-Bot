using Domain.DTOs;
using Microsoft.EntityFrameworkCore;
using TelegramBot.Domain.DTOs;
using TelegramBot.Domain.Entities;

namespace TelegramBot.Services.SubjectService;

public class SubjectService : ISubjectService
{
    private readonly DataContext _context;

    public SubjectService(DataContext context)
    {
        _context = context;
    }

    public async Task<List<GetSubjectDTO>> GetAllSubjects()
    {
        return await _context.Subjects
            .Select(s => new GetSubjectDTO
            {
                Id = s.Id,
                Name = s.Name,
                QuestionCount = s.Questions.Count
            })
            .ToListAsync();
    }

    public async Task<SubjectWithQuestionsDTO> GetSubjectById(int id)
    {
        var subject = await _context.Subjects
            .Include(s => s.Questions)
            .ThenInclude(q => q.Option)
            .FirstOrDefaultAsync(s => s.Id == id);

        if (subject == null)
            return null;

        return new SubjectWithQuestionsDTO
        {
            Id = subject.Id,
            Name = subject.Name,
            Questions = subject.Questions.Select(q => new GetQuestionWithOptionsDTO
            {
                QuestionId = q.Id,
                QuestionText = q.QuestionText,
                FirstOption = q.Option.OptionA,
                SecondOption = q.Option.OptionB,
                ThirdOption = q.Option.OptionC,
                FourthOption = q.Option.OptionD,
                Answer = q.Option.CorrectAnswer,
                SubjectId = q.SubjectId,
                SubjectName = subject.Name
            }).ToList()
        };
    }

    public async Task<GetSubjectDTO> CreateSubject(CreateSubjectDTO createSubject)
    {
        var subject = new Subject
        {
            Name = createSubject.Name
        };

        _context.Subjects.Add(subject);
        await _context.SaveChangesAsync();

        return new GetSubjectDTO
        {
            Id = subject.Id,
            Name = subject.Name,
            QuestionCount = 0
        };
    }

    public async Task<GetSubjectDTO> UpdateSubject(UpdateSubjectDTO updateSubject)
    {
        var subject = await _context.Subjects.FindAsync(updateSubject.Id);
        
        if (subject == null)
            return null;

        subject.Name = updateSubject.Name;
        await _context.SaveChangesAsync();

        return new GetSubjectDTO
        {
            Id = subject.Id,
            Name = subject.Name,
            QuestionCount = subject.Questions.Count
        };
    }

    public async Task<bool> DeleteSubject(int id)
    {
        var subject = await _context.Subjects.FindAsync(id);
        if (subject == null)
            return false;

        _context.Subjects.Remove(subject);
        await _context.SaveChangesAsync();
        return true;
    }
}