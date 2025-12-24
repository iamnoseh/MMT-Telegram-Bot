using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MMT.Domain.Entities;

namespace MMT.Persistence.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.HasKey(u => u.Id);
        
        builder.HasIndex(u => u.ChatId).IsUnique();
        builder.HasIndex(u => u.Score);
        
        builder.Property(u => u.Username).HasMaxLength(100);
        builder.Property(u => u.Name).HasMaxLength(100).IsRequired();
        builder.Property(u => u.PhoneNumber).HasMaxLength(20).IsRequired();
        builder.Property(u => u.City).HasMaxLength(50).IsRequired();
        
        builder.HasMany(u => u.UserResponses)
            .WithOne()
            .HasForeignKey(ur => ur.ChatId)
            .HasPrincipalKey(u => u.ChatId);
    }
}
