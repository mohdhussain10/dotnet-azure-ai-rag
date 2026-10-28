namespace RAG.Domain.Entities
{
    public class ChatMessage
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid ConversationId { get; set; }

        /// <summary>
        /// "user" or "assistant"
        /// </summary>
        public required string Role { get; set; }
       
        public required string Content { get; set; }

        /// <summary>
        /// Optional JSON storing document names/chunks used to ground the answer
        /// </summary>
        public string? Citations { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        
        public Conversations? Conversation { get; set; }
    }
}
