    using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using MMT.Application.Common.Interfaces.Repositories;
using MMT.Persistence.Contexts;

namespace MMT.Persistence.Repositories;

public class UnitOfWork : IUnitOfWork
{
    private readonly ApplicationDbContext _context;
    private IDbContextTransaction? _transaction;
    
    public IUserRepository Users { get; }
    public IQuestionRepository Questions { get; }
    public ISubjectRepository Subjects { get; }
    public IBookRepository Books { get; }
    public IInvitationRepository Invitations { get; }
    public IUserTestSessionRepository TestSessions { get; }
    public IRegistrationSessionRepository RegistrationSessions { get; }
    public IUserStateRepository UserStates { get; }
    public IUserResponseRepository UserResponses { get; }
    
    public UnitOfWork(ApplicationDbContext context)
    {
        _context = context;
        
        Users = new UserRepository(_context);
        Questions = new QuestionRepository(_context);
        Subjects = new SubjectRepository(_context);
        Books = new BookRepository(_context);
        Invitations = new InvitationRepository(_context);
        TestSessions = new UserTestSessionRepository(_context);
        RegistrationSessions = new RegistrationSessionRepository(_context);
        UserStates = new UserStateRepository(_context);
        UserResponses = new UserResponseRepository(_context);
    }
    
    public async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        return await _context.SaveChangesAsync(ct);
    }
    
    public async Task BeginTransactionAsync(CancellationToken ct = default)
    {
        _transaction = await _context.Database.BeginTransactionAsync(ct);
    }
    
    public async Task CommitTransactionAsync(CancellationToken ct = default)
    {
        if (_transaction != null)
        {
            await _transaction.CommitAsync(ct);
            await _transaction.DisposeAsync();
            _transaction = null;
        }
    }
    
    public async Task RollbackTransactionAsync(CancellationToken ct = default)
    {
        if (_transaction != null)
        {
            await _transaction.RollbackAsync(ct);
            await _transaction.DisposeAsync();
            _transaction = null;
        }
    }
    
    public void Dispose()
    {
        _transaction?.Dispose();
        _context.Dispose();
    }
}
