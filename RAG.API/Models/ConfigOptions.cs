using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace RAG.API.Models
{
    public record SupabaseOptions
    {
        public const string SectionName = "Supabase";
        public string Url { get; init; } = string.Empty;
        public string Key { get; init; } = string.Empty;
        public string JwtSecret { get; init; } = string.Empty;

        public string BuildAuthUrl(string endpoint) => $"{Url}/auth/v1/{endpoint.TrimStart("/")}";
    }

    public record AzureStorageOptions
    {
        public const string SectionName = "AzureStorage";

        public string BlobStorageConnectionString { get; init; } = string.Empty;

        public string BlobContainer { get; init; } = string.Empty;

        public string OpenAIEndpoint { get; init; } = string.Empty;

        public string OpenAIKey { get; init; } = string.Empty;
    }

    public record GeminiOptions
    {
        public const string SectionName = "Gemini";

        public string ApiKey { get; init; } = string.Empty;

        public string EmbeddingDeploymentName { get; init; } = "gemini-embedding-001";

        public string ChatModel { get; init; } = "gemini-2.5-flash-lite";

        public string BuildEmbeddingUrl(string endPoint) => $"https://generativelanguage.googleapis.com/v1beta/models/{EmbeddingDeploymentName}:{endPoint}?key={ApiKey}";

        public string BuildChatUrl() => $"https://generativelanguage.googleapis.com/v1beta/models/{ChatModel}:generateContent?key={ApiKey}";
    }

    public static class OptionsExtensions
    {
        public static IServiceCollection AddAppConfigurationOptions(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            services.Configure<SupabaseOptions>(
                configuration.GetSection(SupabaseOptions.SectionName));

            services.Configure<AzureStorageOptions>(
                configuration.GetSection(AzureStorageOptions.SectionName));

            services.Configure<GeminiOptions>(
                configuration.GetSection(GeminiOptions.SectionName));

            return services;
        }
    }



}
