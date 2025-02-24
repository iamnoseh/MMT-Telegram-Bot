namespace data;

using Microsoft.EntityFrameworkCore;
using TelegramBot.Domain.Entities;

public class DataContext:DbContext
{
      public DataContext(DbContextOptions<DataContext> options) : base(options)
    {
    }
       protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseNpgsql("Host=localhost;Port = 5432;Database=TelegramBotDb;Username=postgres;Password=12345");
    }
    
    public DbSet<Question> Questions { get; set; }
    public DbSet<Option> Options { get; set; }
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Option>()
                    .HasOne(o => o.Question)
                    .WithOne(q => q.Option)
                    .HasForeignKey<Question>(q=> q.OptionId);
    }
}