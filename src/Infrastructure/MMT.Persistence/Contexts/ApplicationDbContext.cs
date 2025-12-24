using Microsoft.EntityFrameworkCore;
using MMT.Domain.Entities;

namespace MMT.Persistence.Contexts;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Question> Questions => Set<Question>();
    public DbSet<Subject> Subjects => Set<Subject>();
    public DbSet<Book> Books => Set<Book>();
    public DbSet<BookCategory> BookCategories => Set<BookCategory>();
    public DbSet<Option> Options => Set<Option>();
    public DbSet<UserResponse> UserResponses => Set<UserResponse>();
    public DbSet<DuelGame> DuelGames => Set<DuelGame>();
    public DbSet<Invitation> Invitations => Set<Invitation>();
    public DbSet<UserTestSession> UserTestSessions => Set<UserTestSession>();
    public DbSet<RegistrationSession> RegistrationSessions => Set<RegistrationSession>();
    public DbSet<UserState> UserStates => Set<UserState>();
    
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
        
        modelBuilder.Entity<User>().HasQueryFilter(u => !u.IsDeleted);
        modelBuilder.Entity<Book>().HasQueryFilter(b => !b.IsDeleted && b.IsActive);
    }
}
