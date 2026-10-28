namespace RAG.API.DTOs
{
    public record ChatRequest
    {
        public Guid? ConversationId { get; set; }

        public required string Message { get; set; }
    }
}
