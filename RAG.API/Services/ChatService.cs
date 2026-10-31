using Microsoft.EntityFrameworkCore;
using RAG.API.DTOs;
using RAG.API.Models;
using RAG.Domain.Entities;
using RAG.Infrastructure.Data;
using System.Text.Json;

namespace RAG.API.Services
{
    public interface IchatService
    {
        /// <summary>
        /// Sends user query, retrieves relevant document chunks via hybrid search,
        /// fetches conversation history, calls Gemini, and persists the turn.
        /// </summary>
        Task<AuthResponse> AskAsync(ChatRequest request, CancellationToken cancellationToken = default);

        /// <summary>
        /// Retrieves all active conversations for the current user.
        /// </summary>
        Task<AuthResponse> GetUserConversationsAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Retrieves full message history for a specific conversation.
        /// </summary>
        Task<AuthResponse> GetConversationMessagesAsync(Guid conversationId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Deletes a conversation.
        /// </summary>
        Task<AuthResponse> DeleteConversationAsync(Guid conversationId, CancellationToken cancellationToken = default);

    }
    public class ChatService : IchatService
    {
        private readonly IAzureSearchService _azureSearchService;
        private readonly IEmbeddingService _embeddingService;
        private readonly IGeminiChatService _geminiChatService;
        private readonly ICurrentUserService _currentUserService;
        private readonly AppDbContext _dbContext;
        private readonly ILogger<ChatService> _logger;

        public ChatService(IAzureSearchService azureSearchService, IEmbeddingService embeddingService,
            IGeminiChatService geminiChatService, ICurrentUserService currentUserService,
            AppDbContext dbContext, ILogger<ChatService> logger)
        {
            _azureSearchService = azureSearchService;
            _embeddingService = embeddingService;
            _geminiChatService = geminiChatService;
            _currentUserService = currentUserService;
            _dbContext = dbContext;
            _logger = logger;
        }

        public async Task<AuthResponse> AskAsync(ChatRequest request, CancellationToken cancellationToken = default)
        {
            var userId = _currentUserService.UserId;

            if (!userId.HasValue)
            {
                return new AuthResponse(false, "User is not authenticated.");
            }
            if (string.IsNullOrWhiteSpace(request.Message))
            {
                return new AuthResponse(false, "Message is required.");
            }
            var userMessage = request.Message.Trim();

            try
            {
                Conversations? conversation = null;

                //Old conversation
                if(request.ConversationId.HasValue && request.ConversationId.Value != Guid.Empty)
                {
                    conversation = await _dbContext.Conversations
                        .FirstOrDefaultAsync(c => c.Id == request.ConversationId.Value && c.UserId == userId.Value, cancellationToken);
                }

                //It is fresh conversation. Will create title and guid and insert to DB
                if(conversation == null)
                {
                    var title = userMessage;

                    if (title.Length > 40)  title = title.Substring(0,37) + "...";

                    conversation = new Conversations
                    {
                        Id = Guid.NewGuid(),
                        UserId = userId.Value,
                        Title = title,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };
                    _dbContext.Conversations.Add(conversation);
                    await _dbContext.SaveChangesAsync(cancellationToken);

                }

                //get latest 6 message history
                var recentMessages = await _dbContext.ChatMessages.Where(m => m.ConversationId == conversation.Id).
                    OrderByDescending(m => m.CreatedAt).Take(6).OrderBy(c => c.CreatedAt).Select(m => new ValueTuple<string, string>(m.Role, m.Content)).ToListAsync(cancellationToken);


                //Embedding User Question (786 dim vector) 
                //Required because using Hybrid search. for only full text search it is not required

                var queryVectorMemory = await _embeddingService.GenerateEmbeddingAsync(userMessage, cancellationToken);

                var queryVector = queryVectorMemory.ToArray();

                //Get relevant chunks from Azure AI Search
                var relevantChunks = await _azureSearchService.SearchRelevantChunksAsync(
                    queryText: userMessage,
                    queryVector: queryVector,
                    userId: userId.Value.ToString(),
                    top: 4,
                    cancellationToken: cancellationToken);

                _logger.LogInformation("Retrieved {Count} relevant chunks for conversation {ConversationId}",
                    relevantChunks.Count, conversation.Id);
                var llmresponse = await _geminiChatService.GenerateGroundedAnswerAsync(userQuestion: userMessage,
                    contextChunks: relevantChunks, conversationHistory: recentMessages, cancellationToken: cancellationToken);


                var citations = relevantChunks.Select(c => new
                {
                    fileName = c.FileName,
                    chunkIndex = c.ChunkIndex,
                    preview = c.Content.Length > 150 ? c.Content.Substring(0, 147) + "..." : c.Content
                }).ToList();
                var userMessageToStore = new ChatMessage
                {
                    Id = Guid.NewGuid(),
                    ConversationId = conversation.Id,
                    Role = "user",
                    Content = request.Message,
                    CreatedAt = DateTime.UtcNow
                };

                var assistantMessageToStore = new ChatMessage
                {
                    Id = Guid.NewGuid(),
                    ConversationId = conversation.Id,
                    Role = "assistant",
                    Content = llmresponse,
                    Citations = citations.Count > 0 ? JsonSerializer.Serialize(citations) : null,
                    CreatedAt = DateTime.UtcNow
                };
                conversation.UpdatedAt = DateTime.UtcNow;

                _dbContext.ChatMessages.AddRange(userMessageToStore, assistantMessageToStore);
                await _dbContext.SaveChangesAsync(cancellationToken);

                return new AuthResponse(true, Data: new
                {
                    conversationId = conversation.Id,
                    conversationTitle = conversation.Title,
                    answer = llmresponse,
                    citations = citations
                });
            }
            catch(InvalidOperationException invalidException)
            {
                _logger.LogError(invalidException, "Failed to reach AI service for user {UserId}", userId);
                return new AuthResponse(false, invalidException.Message);
            }
            catch(Exception ex)
            {
                _logger.LogError(ex, "Failed to process chat query for user {UserId}", userId);
                return new AuthResponse(false, "An error occurred while generating your answer. Please try again.");
            }


        }

        /// <summary>
        /// Delete Conversation
        /// </summary>
        /// <param name="conversationId"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<AuthResponse> DeleteConversationAsync(Guid conversationId, CancellationToken cancellationToken = default)
        {
            var userId = _currentUserService.UserId;
            if (userId == null)
                return new AuthResponse(false, "User is not authenticated.");

            if (conversationId == Guid.Empty)
                return new AuthResponse(false, "Conversation Id is not valid");
            try
            {
                var conversation = await _dbContext.Conversations.FirstOrDefaultAsync(c => c.Id == conversationId && c.UserId == userId.Value, cancellationToken);

                if(conversation == null)
                    return new AuthResponse(false, "Conversation not found.");

                conversation.IsDeleted = true;
                conversation.DeletedAt = DateTime.UtcNow;

                await _dbContext.SaveChangesAsync(cancellationToken);
                return new AuthResponse(true, Data: new { message = "Conversation deleted successfully." });

            }
            catch(Exception ex)
            {
                _logger.LogError(ex, "Failed to delete conversation {ConversationId}", conversationId);
                return new AuthResponse(false, "Failed to delete conversation.");
            }
        }

        /// <summary>
        /// Get Conversation Messages
        /// </summary>
        /// <param name="conversationId"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<AuthResponse> GetConversationMessagesAsync(Guid conversationId, CancellationToken cancellationToken = default)
        {
            var userId = _currentUserService.UserId;
            if (userId == null)
                return new AuthResponse(false, "User is not authenticated.");
            try
            {
                if(conversationId == Guid.Empty)
                    return new AuthResponse(false, "Conversation Id is not valid");

                var conversation = await _dbContext.Conversations.AsNoTracking().FirstOrDefaultAsync(c => c.Id == conversationId && c.UserId == userId);

                if(conversation == null)
                    return new AuthResponse(false, "Conversation not found.");

                var messages = await _dbContext.ChatMessages.AsNoTracking().Where(m => m.ConversationId == conversation.Id)
                    .OrderBy(m => m.CreatedAt).Select(m => new
                    {
                        m.Id,
                        m.Role,
                        m.Content,
                        m.Citations,
                        m.CreatedAt

                    }).ToListAsync(cancellationToken);

                return new AuthResponse(true, Data: new
                {
                    conversationId = conversation.Id,
                    title = conversation.Title,
                    messages
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to fetch messages for conversation {ConversationId}", conversationId);
                return new AuthResponse(false, "Failed to load conversation history.");
            }
        }

        /// <summary>
        /// Get conversations
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<AuthResponse> GetUserConversationsAsync(CancellationToken cancellationToken = default)
        {
            var userId = _currentUserService.UserId;
            if (userId == null)
                return new AuthResponse(false, "User is not authenticated.");
            try
            {
                var conversations = _dbContext.Conversations.AsNoTracking().Where(c => c.UserId == userId.Value).OrderByDescending(c => c.CreatedAt)
                    .Select(c => new
                    {
                        c.Id,
                        c.Title,
                        c.CreatedAt,
                        c.UpdatedAt
                    }).ToListAsync(cancellationToken);

                return new AuthResponse(true, Data: new { conversations});
            }
            catch(Exception ex)
            {
                _logger.LogError(ex, "Failed to fetch conversations for user {UserId}", userId);
                return new AuthResponse(false, "Failed to load conversations.");
            }
        }
    }
}
