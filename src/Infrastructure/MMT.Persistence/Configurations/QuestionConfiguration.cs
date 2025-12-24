using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MMT.Domain.Entities;

namespace MMT.Persistence.Configurations;

public class QuestionConfiguration : IEntityTypeConfiguration<Question>
{
    public void Configure(EntityTypeBuilder<Question> builder)
    {
        builder.HasKey(q => q.Id);
        
        builder.HasIndex(q => q.SubjectId);
        
        builder.Property(q => q.QuestionText).IsRequired();
        
        builder.HasOne(q => q.Subject)
            .WithMany(s => s.Questions)
            .HasForeignKey(q => q.SubjectId);
            
        builder.HasOne(q => q.Option)
            .WithOne(o => o.Question)
            .HasForeignKey<Option>(o => o.QuestionId);
    }
}
