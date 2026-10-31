using Microsoft.Extensions.Options;
using RAG.API.Models;
using System.Net;
using System.Text.Json.Serialization;
namespace RAG.API.Services
{
    public interface IGeminiChatService
    {
        Task<string> GenerateGroundedAnswerAsync(string userQuestion, List<DocumentChunkModel> contextChunks,
            List<(string role, string content)> conversationHistory, CancellationToken cancellationToken = default);
    }
    public class GeminiChatService : IGeminiChatService
    {
        private readonly HttpClient _httpClient;

        private readonly GeminiOptions _geminiOptions;

        private readonly ILogger<GeminiChatService> _logger;


        public GeminiChatService(HttpClient httpClient, IOptions<GeminiOptions> geminiOptions, ILogger<GeminiChatService> logger)
        {
            _geminiOptions = geminiOptions.Value;
            _httpClient = httpClient;
            _logger = logger;
        }



        public async Task<string> GenerateGroundedAnswerAsync(string userQuestion, List<DocumentChunkModel> contextChunks, List<(string role, string content)> conversationHistory, CancellationToken cancellationToken = default)
        {

            var modelName = _geminiOptions.ChatModel;

            var contextBuilder = new System.Text.StringBuilder();

            if (contextChunks.Count > 0)
            {
                contextBuilder.AppendLine("### RETRIEVED DOCUMENT CONTEXT:");

                foreach (var chunk in contextChunks)
                {
                    contextBuilder.AppendLine($"[Document: {chunk.FileName}, Chunk: {chunk.ChunkIndex}]");

                    contextBuilder.AppendLine(chunk.Content);
                    contextBuilder.AppendLine("---");
                }
            }
            else
            {
                contextBuilder.AppendLine("No relevant documents found in the user's workspace.");
            }

            var systemInstruction = new
            {
                parts = new[]
              {
                    new
                    {
                        text = @"You are a factual, accurate AI assistant for a document-based RAG application.
Answer the user's question using ONLY the provided Document Context.
Rules:
1. If the answer cannot be found in the provided context, politely state: 'Based on your uploaded documents, I do not have enough information to answer this question.'
2. Do not invent, speculate, or hallucinate facts not present in the context.
3. Always cite the document name (e.g. [Document: filename.pdf]) when referencing facts."
                    }
                }
            };

            //Appending conversation history up to 6 messages
            var contents = new List<object>();

            foreach (var (role, content) in conversationHistory)
            {
                contents.Add(new
                {
                    role = role.ToLowerInvariant() == "assistant" ? "model" : "user",
                    parts = new[] { new { text = content } }
                });

            }
            var currentTurnPrompt = $@"{contextBuilder}
UserQuestion: {userQuestion}";
            contents.Add(new
            {
                role = "user",
                parts = new[] { new { text = currentTurnPrompt } }

            });

            var payload = new
            {
                system_instruction = systemInstruction,
                contents = contents,
                generationConfig = new
                {
                    temperature = 0.2, // Low temperature for high accuracy
                    maxOutputTokens = 1000
                }
            };
            HttpResponseMessage response;
            try
            {
                response = await _httpClient.PostAsJsonAsync(_geminiOptions.BuildChatUrl(), payload, cancellationToken);
            }
            catch (HttpRequestException httpEx)
            {
                _logger.LogError(httpEx, "Network connectivity error connecting to Gemini API.");
                throw new InvalidOperationException("Unable to reach the AI service. Please check your connection and try again.", httpEx);
            }
            catch (OperationCanceledException timeoutEx) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogError(timeoutEx, "Gemini API request timed out.");
                throw new InvalidOperationException("The AI service took too long to respond. Please try again.", timeoutEx);
            }

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("Gemini API returned error ({StatusCode}): {Body}", response.StatusCode, errorBody);

                switch (response.StatusCode)
                {
                    case HttpStatusCode.TooManyRequests:
                        throw new InvalidOperationException("AI rate limit reached. Please wait 15–30 seconds before asking again.");
                        
                    case HttpStatusCode.ServiceUnavailable:
                    case HttpStatusCode.BadGateway:
                    case HttpStatusCode.GatewayTimeout:
                        throw new InvalidOperationException("The AI model is temporarily overloaded. Please try again in a few moments.");
                    case HttpStatusCode.NotFound:
                        throw new InvalidOperationException($"The configured chat model '{_geminiOptions.ChatModel}' was not found. Please verify configuration.");
                    default:
                        throw new InvalidOperationException("The AI service encountered an error processing your request. Please try again.");

                }
            }

            var result = await response.Content.ReadFromJsonAsync<GeminiGenerateContentResponse>(cancellationToken);
            var candidate = result?.Candidates?.FirstOrDefault();
            if (candidate == null)
            {
                _logger.LogWarning("Gemini returned 200 OK but with 0 candidates.");
                throw new InvalidOperationException("The AI was unable to generate an answer for this prompt.");
            }
            
            if (candidate.FinishReason?.Equals("SAFETY", StringComparison.OrdinalIgnoreCase) == true)
            {
                _logger.LogWarning("Gemini blocked response due to SAFETY filter.");
                throw new InvalidOperationException("This question could not be answered because it triggered content safety filters.");
            }
            var replyText = candidate.Content?.Parts?.FirstOrDefault()?.Text;
            if (string.IsNullOrWhiteSpace(replyText))
            {
                _logger.LogWarning("Gemini returned candidate with empty text. FinishReason: {Reason}", candidate.FinishReason);
                throw new InvalidOperationException("The AI returned an empty response. Please try rephrasing your question.");
            }
            return replyText.Trim();
        }
        #region Response Models
        private sealed class GeminiGenerateContentResponse
        {
            [JsonPropertyName("candidates")]
            public List<GeminiCandidate>? Candidates { get; set; }
        }
        private sealed class GeminiCandidate
        {
            [JsonPropertyName("content")]
            public GeminiContent? Content { get; set; }

            [JsonPropertyName("finishReason")]
            public string? FinishReason { get; set; }
        }
        private sealed class GeminiContent
        {
            [JsonPropertyName("parts")]
            public List<GeminiPart>? Parts { get; set; }
        }
        private sealed class GeminiPart
        {
            [JsonPropertyName("text")]
            public string? Text { get; set; }
        }
        #endregion
    }

}
