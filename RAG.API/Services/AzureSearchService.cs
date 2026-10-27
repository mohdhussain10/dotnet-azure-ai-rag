using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Indexes.Models;
using Azure.Search.Documents.Models;
using RAG.API.Models;

namespace RAG.API.Services
{
    public interface IAzureSearchService
    {
        /// <summary>
        /// It create the index i ai search 
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        Task EnsureIndexExistsAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Insert a batch of documents chunks and their vectors in to azure ai search
        /// </summary>
        /// <param name="chunks"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        Task<int> InsertChunksAsync(IEnumerable<DocumentChunkModel> chunks, CancellationToken cancellationToken = default);

        /// <summary>
        /// Removes all chunks of a deleted document from the search index.
        /// </summary>
        /// <param name="documentId"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        Task DeleteChunksByDocumentIdAsync(Guid documentId, CancellationToken cancellationToken = default);

    }
    public class AzureSearchService : IAzureSearchService
    {
        private readonly SearchClient _searchClient;

        private readonly SearchIndexClient _indexClient;

        private readonly string _indexName;

        private readonly ILogger<AzureSearchService> _logger;

        public AzureSearchService(IConfiguration config, ILogger<AzureSearchService> logger) 
        {
            _logger = logger;


            var endpointStr = config["AzureAISearch:Endpoint"] ?? config["AzureSearch:Endpoint"];
            var apiKey = config["AzureAISearch:ApiKey"] ?? config["AzureSearch:ApiKey"];
            _indexName = config["AzureAISearch:IndexName"] ?? config["AzureSearch:IndexName"] ?? "rag-documents-index";

            if (string.IsNullOrWhiteSpace(endpointStr) || string.IsNullOrWhiteSpace(apiKey))
            {
                throw new InvalidOperationException("Azure Search endpoint and ApiKey must be configured.");
            }

            var endpoint = new Uri(endpointStr);
            var credential = new AzureKeyCredential(apiKey);
            _indexClient = new SearchIndexClient(endpoint, credential);
            _searchClient = new SearchClient(endpoint, _indexName, credential);
        }

        /// <summary>
        /// Automatically creates the index in Azure AI search with 768 vector dimensions if it doesn't exist
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task EnsureIndexExistsAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                var response = await _indexClient.GetIndexNamesAsync(cancellationToken).ToListAsync(cancellationToken);

                //Index already exist
                if (response.Contains(_indexName)) 
                {
                    _logger.LogInformation("Index of '{IndexName}' is already exist.. returning", _indexName);
                    return;
                }

                _logger.LogInformation("Creating Azure AI Search index '{IndexName}' with 768 vector dimensions...", _indexName);

                var fieldBuilder = new FieldBuilder();

                //it will create search fields to pass to index while creating index.
                var searchFields = fieldBuilder.Build(typeof(DocumentChunkModel));

                var searchIndex = new SearchIndex(_indexName, searchFields)
                {
                    VectorSearch = new VectorSearch
                    {
                        Algorithms =
                        {
                            new HnswAlgorithmConfiguration("hsnw-algorithm")
                            {
                                Parameters = new HnswParameters{Metric = VectorSearchAlgorithmMetric.Cosine}
                            }
                        },
                        Profiles =
                        {
                            new VectorSearchProfile("gemini-vector-profile", "hsnw-algorithm")
                        }
                    },
                    
                };

                await _indexClient.CreateIndexAsync(searchIndex, cancellationToken);

                _logger.LogInformation("Azure AI Search index '{IndexName}' created successfully.", _indexName);
            }
            catch(Exception ex)
            {
                _logger.LogError(ex, "Failed to ensure Azure AI Search index exists.");
                throw;

            }
            
        }

        /// <summary>
        /// Insert a batch of documents chunks and their vectors in to azure ai search
        /// </summary>
        /// <param name="chunks"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<int> InsertChunksAsync(IEnumerable<DocumentChunkModel> chunks, CancellationToken cancellationToken = default)
        {
            var chunkList = chunks.ToList();

            if (chunkList.Count == 0) return 0;

            try
            {
                await EnsureIndexExistsAsync(cancellationToken);

                //Azure AI search batch all the chunk to upsert them at a time. like batch of 1000 documents or cchunks to be upsert in a single request
                //it check if the document key is not available in azure ai search it will be upsert there.
                var batch = IndexDocumentsBatch.Upload(chunkList);

                var result = await _searchClient.IndexDocumentsAsync(batch, cancellationToken: cancellationToken);

                int successCount = result.Value.Results.Count(r => r.Succeeded);

                _logger.LogInformation("Indexed {Count}/{Total} chunks into Azure Search.", successCount, chunkList.Count);

                return successCount;
            }
            catch(Exception ex)
            {
                _logger.LogError(ex, "Failed to upsert chunks into Azure Search.");
                throw;
            }
        }

        /// <summary>
        /// Removes all chunks of a deleted document from the search index.
        /// </summary>
        /// <param name="documentId"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task DeleteChunksByDocumentIdAsync(Guid documentId, CancellationToken cancellationToken = default)
        {
            try
            {
                var options = new SearchOptions
                {
                    Filter = $"documentId eq '{documentId}'",
                    Select = { "id" }
                };
                var searchResponse = await _searchClient.SearchAsync<SearchDocument>("*", options, cancellationToken);
                var keysToDelete = new List<string>();
                await foreach (var doc in searchResponse.Value.GetResultsAsync())
                {
                    keysToDelete.Add(doc.Document["id"].ToString()!);
                }
                if (keysToDelete.Count > 0)
                {
                    await _searchClient.DeleteDocumentsAsync("id", keysToDelete, cancellationToken: cancellationToken);
                    _logger.LogInformation("Deleted {Count} chunks from search index for document {DocumentId}.", keysToDelete.Count, documentId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete search index chunks for document {DocumentId}", documentId);
            }
        }
    }
}
