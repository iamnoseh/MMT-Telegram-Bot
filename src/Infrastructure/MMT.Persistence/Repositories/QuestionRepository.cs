using Microsoft.EntityFrameworkCore;
using MMT.Application.Common.Interfaces.Repositories;
using MMT.Domain.Entities;
using MMT.Persistence.Contexts;

namespace MMT.Persistence.Repositories;

public class QuestionRepository(ApplicationDbContext context) : IQuestionRepository
{
    public async Task<Question?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        return await context.Questions
            .Include(q => q.Option)
            .Include(q => q.Subject)
            .FirstOrDefaultAsync(q => q.Id == id, ct);
    }
    
    public async Task<Question?> GetRandomBySubjectAsync(int subjectId, CancellationToken ct = default)
    {
        return await context.Questions
            .Include(q => q.Option)
            .Where(q => q.SubjectId == subjectId)
            .OrderBy(q => Guid.NewGuid())
            .FirstOrDefaultAsync(ct);
    }
    
    public async Task<List<Question>> GetBySubjectAsync(int subjectId, CancellationToken ct = default)
    {
        return await context.Questions
            .Include(q => q.Option)
            .Where(q => q.SubjectId == subjectId)
            .ToListAsync(ct);
    }
    
    public async Task<bool> ExistsAsync(int subjectId, string questionText, CancellationToken ct = default)
    {
        return await context.Questions
            .AnyAsync(q => q.SubjectId == subjectId 
                        && q.QuestionText.Trim().ToLower() == questionText.Trim().ToLower(), 
                ct);
    }
    
    public async Task AddAsync(Question question, CancellationToken ct = default)
    {
        await context.Questions.AddAsync(question, ct);
    }
    
    public async Task AddRangeAsync(List<Question> questions, CancellationToken ct = default)
    {
        await context.Questions.AddRangeAsync(questions, ct);
    }
    
    public void Update(Question question)
    {
        context.Questions.Update(question);
    }
    
    public void Delete(Question question)
    {
        context.Questions.Remove(question);
    }
    
    public async Task<int> GetCountBySubjectAsync(int subjectId, CancellationToken ct = default)
    {
        return await context.Questions.CountAsync(q => q.SubjectId == subjectId, ct);
    }
}
