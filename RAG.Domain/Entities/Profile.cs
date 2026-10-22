namespace RAG.Domain.Entities
{
    public class Profile
    {
        public Guid Id { get; set; }
        public required string Email { get; set; }
        public string? FullName { get; set; }        // Auto-mapped to 'full_name'
        public DateTime CreatedAt { get; set; }      // Auto-mapped to 'created_at'

        public ICollection<UserDocument> Documents { get; set; } = new List<UserDocument>();
    
    }
}
