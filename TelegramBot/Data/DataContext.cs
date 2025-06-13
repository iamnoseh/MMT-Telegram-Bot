using Microsoft.EntityFrameworkCore;
using TelegramBot.Domain.Entities;

public class DataContext : DbContext
{
    public DataContext(DbContextOptions<DataContext> options) : base(options) { }

    public DbSet<Subject> Subjects { get; set; }
    public DbSet<Question> Questions { get; set; }
    public DbSet<Invitation> Invitations { get; set; }
    public DbSet<Option> Options { get; set; }
    public DbSet<User> Users { get; set; }
    public DbSet<UserResponse> UserResponses { get; set; }
    public DbSet<Question2Admin> QuestionsToAdmin { get; set; }
    public DbSet<DuelGame> DuelGames { get; set; }
    public DbSet<UserReferral> UserReferrals { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Алоқаи Question ва Subject
        modelBuilder.Entity<Question>()
            .HasOne(q => q.Subject)
            .WithMany(s => s.Questions)
            .HasForeignKey(q => q.SubjectId);

        // Алоқаи Question ва Option (1:1)
        modelBuilder.Entity<Question>()
            .HasOne(q => q.Option)
            .WithOne(o => o.Question)
            .HasForeignKey<Option>(o => o.QuestionId);

        // Алоқаи Question ва UserResponse
        modelBuilder.Entity<UserResponse>()
            .HasOne(ur => ur.Question)
            .WithMany(q => q.UserResponses)
            .HasForeignKey(ur => ur.QuestionId);
    }
}