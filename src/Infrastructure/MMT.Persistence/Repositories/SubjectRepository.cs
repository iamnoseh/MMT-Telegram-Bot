using Microsoft.EntityFrameworkCore;
using MMT.Application.Common.Interfaces.Repositories;
using MMT.Domain.Entities;
using MMT.Persistence.Contexts;

namespace MMT.Persistence.Repositories;

public class SubjectRepository(ApplicationDbContext context) : ISubjectRepository
{
    public async Task<Subject?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        return await context.Subjects.FirstOrDefaultAsync(s => s.Id == id, ct);
    }
    
    public async Task<List<Subject>> GetAllAsync(CancellationToken ct = default)
    {
        return await context.Subjects.ToListAsync(ct);
    }
    
    public async Task AddAsync(Subject subject, CancellationToken ct = default)
    {
        await context.Subjects.AddAsync(subject, ct);
    }
    
    public void Update(Subject subject)
    {
        context.Subjects.Update(subject);
    }
    
    public async Task<bool> ExistsAsync(int id, CancellationToken ct = default)
    {
        return await context.Subjects.AnyAsync(s => s.Id == id, ct);
    }
}
