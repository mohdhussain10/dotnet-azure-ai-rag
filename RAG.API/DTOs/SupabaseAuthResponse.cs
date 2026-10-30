using System.Text.Json.Serialization;

namespace RAG.API.DTOs
{
    public class SupabaseAuthResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }

        [JsonPropertyName("token_type")]
        public string? TokenType { get; set; }

        [JsonPropertyName("refresh_token")]
        public string? RefrestToken { get; set; }

        [JsonPropertyName("expires_in")]
        public int? ExpiresIn { get; set; }

        [JsonPropertyName("user")]
        public SupabaseUser? User { get; set; }

    }

    public class SupabaseUser
    {
        [JsonPropertyName("id")]
        public Guid Id { get; set; }

        [JsonPropertyName("email")]
        public string? Email { get; set; }

        [JsonPropertyName("user_metadata")]
        public SupabaseUserMetadata? UserMetadata { get; set; }
    }
    
    public class SupabaseUserMetadata
    {
        [JsonPropertyName("full_name")]
        public string? FullName { get; set; }
    }

}
