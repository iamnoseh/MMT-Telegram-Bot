namespace MMT.Application.Common.Interfaces.Repositories;

public interface IUnitOfWork : IDisposable
{
    IUserRepository Users { get; }
    IQuestionRepository Questions { get; }
    ISubjectRepository Subjects { get; }
    IBookRepository Books { get; }
    IInvitationRepository Invitations { get; }
    IUserTestSessionRepository TestSessions { get; }
    IRegistrationSessionRepository RegistrationSessions { get; }
    IUserStateRepository UserStates { get; }
    
    Task<int> SaveChangesAsync(CancellationToken ct = default);
    Task BeginTransactionAsync(CancellationToken ct = default);
    Task CommitTransactionAsync(CancellationToken ct = default);
    Task RollbackTransactionAsync(CancellationToken ct = default);
}
