using System;
using System.Collections.Generic;
using System.Text;

namespace RAG.Domain.Entities
{
    public class UserDocument
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid UserId { get; set; }

        public required string FileName { get; set; }

        public required string BlobUrl { get; set; }

        public int ChunkCount { get; set; }

        public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

        public Profile? Profile { get; set; }
    }
}
