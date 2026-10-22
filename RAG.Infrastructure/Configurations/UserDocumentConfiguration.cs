using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RAG.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace RAG.Infrastructure.Configurations
{
    public class UserDocumentConfiguration : IEntityTypeConfiguration<UserDocument>
    {
        public void Configure(EntityTypeBuilder<UserDocument> builder)
        {
            builder.ToTable("user_documents");

            builder.HasKey(d => d.Id);

            builder.Property(d => d.FileName)
                   .IsRequired()
                   .HasMaxLength(255);

            builder.Property(d => d.BlobUrl)
                   .IsRequired();

            // Foreign Key relationship with Cascade Delete
            builder.HasOne(d => d.Profile)
                   .WithMany(p => p.Documents)
                   .HasForeignKey(d => d.UserId)
                   .OnDelete(DeleteBehavior.Cascade);
        }
    }
}
