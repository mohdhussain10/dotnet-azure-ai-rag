using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RAG.Domain.Entities;

namespace RAG.Infrastructure.Configurations
{
    public class ProfileConfiguration : IEntityTypeConfiguration<Profile>
    {
        public void Configure(EntityTypeBuilder<Profile> builder)
        {
            builder.ToTable("profiles");

            builder.HasKey(p => p.Id);

            builder.Property(p => p.Email)
                   .IsRequired()
                   .HasMaxLength(256);

            builder.HasIndex(p => p.Email)
                   .IsUnique();
        }
    }
}

