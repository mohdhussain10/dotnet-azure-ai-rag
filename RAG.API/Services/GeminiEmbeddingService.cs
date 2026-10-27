using Microsoft.Extensions.Options;
using RAG.API.Models;
using System.Text.Json.Serialization;

namespace RAG.API.Services
{
    public interface IEmbeddingService
    {
        /// <summary>
        /// Generates an embedding vector for a single string.
        /// </summary>
        Task<ReadOnlyMemory<float>> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Generates embedding vectors for a batch of text chunks in ONE API call.
        /// </summary>
        Task<IReadOnlyList<ReadOnlyMemory<float>>> GenerateEmbeddingsAsync(IEnumerable<string> texts, CancellationToken cancellationToken = default);
    }
    public class GeminiEmbeddingService : IEmbeddingService
    {
        private readonly HttpClient _httpClient;
        private readonly GeminiOptions _geminiOptions;
        private readonly ILogger<GeminiEmbeddingService> _logger;

        public GeminiEmbeddingService(HttpClient httpClient, IOptions<GeminiOptions> geminiOptions, ILogger<GeminiEmbeddingService> logger)
        {
            _httpClient = httpClient;
            _geminiOptions = geminiOptions.Value;
            _logger = logger;
        }

        /// <summary>
        /// for single message processing. Written for chat message embedding
        /// </summary>
        /// <param name="text"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException"></exception>
        public async Task<ReadOnlyMemory<float>> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
        {
            var url = _geminiOptions.BuildUrl("embedContent");
            var request = new
            {
                model = $"models/{_geminiOptions.EmbeddingDeploymentName}",
                content = new
                {
                    parts = new[] { new { text } }
                },
                outputDimensionality = 768
            };

            var response = await _httpClient.PostAsJsonAsync(url, request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("Gemini embedding failed: {Error}", error);
                throw new InvalidOperationException($"Gemini embedding failed: {error}");
            }

            var result = await response.Content.ReadFromJsonAsync<GeminiSingleResponse>(cancellationToken);
            return result?.Embedding?.Values != null
                ? new ReadOnlyMemory<float>(result.Embedding.Values)
                : ReadOnlyMemory<float>.Empty;
        }

        /// <summary>
        /// For Batch processing
        /// </summary>
        /// <param name="texts"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException"></exception>
        public async Task<IReadOnlyList<ReadOnlyMemory<float>>> GenerateEmbeddingsAsync(IEnumerable<string> texts, CancellationToken cancellationToken = default)
        {
            if (texts.Count() == 0) return Array.Empty<ReadOnlyMemory<float>>();

            var batchUrl = _geminiOptions.BuildUrl("batchEmbedContents");
            var finalList = new List<ReadOnlyMemory<float>>();

            //Gemini allow max 100 request per batch

            const int maxBatchSize = 100;
            foreach (var batch in texts.Chunk(maxBatchSize))
            {

                //it is like sequence of single-item request
                var requests = batch.Select(t => new
                {
                    model = $"models/{_geminiOptions.EmbeddingDeploymentName}",
                    content = new
                    {
                        parts = new[] { new { text = t } }
                    },
                    outputDimensionality = 768 // Default is 3072 dimensions
                });

                var response = await _httpClient.PostAsJsonAsync(batchUrl, new { requests }, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    var error = await response.Content.ReadAsStringAsync(cancellationToken);
                    _logger.LogError("Gemini batch embedding failed: {Error}", error);
                    throw new InvalidOperationException($"Gemini batch embedding failed: {error}");

                }

                var result = await response.Content.ReadFromJsonAsync<GeminiBatchResponse>(cancellationToken);



                if (result?.Embeddings != null)
                {
                    foreach (var emb in result.Embeddings)
                    {
                        if (emb.Values != null)
                        {
                            finalList.Add(new ReadOnlyMemory<float>(emb.Values));
                        }
                    }
                }
            }
            return finalList;

        }

        #region Response Models
        private sealed class GeminiBatchResponse
        {
            [JsonPropertyName("embeddings")]
            public List<GeminiEmbeddingData>? Embeddings { get; set; }
        }

        private sealed class GeminiSingleResponse
        {
            [JsonPropertyName("embedding")]
            public GeminiEmbeddingData? Embedding { get; set; }
        }
        private sealed class GeminiEmbeddingData
        {
            [JsonPropertyName("values")]
            public float[]? Values { get; set; }
        }
        #endregion



    }


}
