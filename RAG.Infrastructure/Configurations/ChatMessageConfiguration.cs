using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RAG.Domain.Entities;

namespace RAG.Infrastructure.Configurations
{
    public class ChatMessageConfiguration : IEntityTypeConfiguration<ChatMessage>
    {
        public void Configure(EntityTypeBuilder<ChatMessage> builder)
        {
            builder.ToTable("ChatMessage");

            builder.HasKey(c => c.Id);

            builder.Property(c => c.Role).IsRequired().HasMaxLength(20);

            builder.Property(c => c.Content).IsRequired();

            builder.HasOne(c => c.Conversation).WithMany().HasForeignKey(c => c.ConversationId).OnDelete(DeleteBehavior.Cascade);

            //index of chronological message retreival
            builder.HasIndex(m => new { m.ConversationId, m.CreatedAt });

        }
    }
}
