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

            //services.Configure<EmbeddingOptions>(
            //    configuration.GetSection(EmbeddingOptions.SectionName));

            return services;
        }
    }



}
