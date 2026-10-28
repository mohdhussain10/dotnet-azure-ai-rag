using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RAG.Domain.Entities;

namespace RAG.Infrastructure.Configurations
{
    public class ConversationConfiguration : IEntityTypeConfiguration<Conversations>
    {
        public void Configure(EntityTypeBuilder<Conversations> builder)
        {
            builder.ToTable("Conversations");

            builder.HasKey(c => c.Id);

            builder.Property(c => c.Title).IsRequired().HasMaxLength(200);

            builder.HasOne(c => c.Profile).WithMany()
                .HasForeignKey(c => c.UserId).OnDelete(DeleteBehavior.Cascade);

            builder.HasQueryFilter(c => !c.IsDeleted);

            //Index for quick listing of conversations based on user id and last updated time
            builder.HasIndex(c => new { c.UserId, c.UpdatedAt });
        }
    }
}
