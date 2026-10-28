namespace RAG.Domain.Entities
{
    public class Conversations
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public string Title { get; set; } = "New Chat";

        public Guid UserId { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        public bool IsDeleted { get; set; } = false;
        public DateTime? DeletedAt { get; set; }
       
        public Profile? Profile { get; set; }
        public ICollection<ChatMessage> Messages { get; set; } = new List<ChatMessage>();

    }
}
