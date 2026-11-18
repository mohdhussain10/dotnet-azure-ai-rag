namespace RAG.Domain.Entities
{
    public class OutboxEvent
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string AggregateType { get; set; } = "UserDocument";
        public Guid AggregateId { get; set; }
        public string EventType { get; set; } = "FileUploaded";
        public string PayloadJson { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? ProcessedAt { get; set; }
        public bool IsProcessed { get; set; } = false;
        public int RetryCount { get; set; } = 0;
        public string? Error { get; set; }
    }
}
