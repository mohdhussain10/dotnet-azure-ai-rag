using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.EntityFrameworkCore;
using RAG.API.Models;
using RAG.Domain.Entities;
using RAG.Infrastructure.Data;
using System.Net;
using System.Security.Cryptography;

namespace RAG.API.Services
{
    public interface IFileService
    {
        /// <summary>
        /// To upload file
        /// </summary>
        /// <param name="request"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        Task<AuthResponse> Upload(IFormFile? file, CancellationToken cancellationToken = default);

        /// <summary>
        /// To remove file from Azure Blob
        /// </summary>
        /// <param name="documentId"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        Task<AuthResponse> Remove(Guid documentId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Get all files for the user
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        Task<AuthResponse> GetAllFiles(CancellationToken cancellationToken = default);

    }

    public class FileService : IFileService
    {
        /// <summary>
        /// To check Allowed Document Extensions
        /// </summary>
        private static HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase) { ".pdf", ".docx", ".txt", ".md", ".csv" };

        /// <summary>
        /// To check file max size
        /// </summary>
        private const long MaxFileSize = 25 * 1024 * 1024;

        private readonly BlobContainerClient _containerClient;

        private readonly ILogger<FileService> _logger;

        private readonly ICurrentUserService _currentUserService;

        private readonly AppDbContext _dbContext;

        private readonly ITextExtractorService _textExtractorService;
        private readonly IChunkingService _chunkingService;

        private readonly IEmbeddingService _embeddingService;

        private readonly IAzureSearchService _azureSearchService;




        public FileService(BlobContainerClient containerClient, ILogger<FileService> logger,
            ICurrentUserService currentUserService, AppDbContext dbContext,
            ITextExtractorService textExtractorService, IChunkingService chunkingService,
            IEmbeddingService embeddingService, IAzureSearchService azureSearchService )
        {
            _containerClient = containerClient;
            _logger = logger;
            _currentUserService = currentUserService;
            _dbContext = dbContext;
            _textExtractorService = textExtractorService;
            _chunkingService = chunkingService;
            _embeddingService = embeddingService;
            _azureSearchService = azureSearchService;
        }
        public async Task<AuthResponse> Upload(IFormFile? file, CancellationToken cancellationToken = default)
        {
            var userId = _currentUserService.UserId;
            Guid? userDocumentId = null;
            BlobClient? uploadedBlobClient = null;
            var isChunksUploadedToAzureAI = false;
            try
            {
                #region Prequisites checks and variables declaration
                if (userId == null) return new AuthResponse(false, "User is not authenticated.");

                if (file!.Length > MaxFileSize)
                    return new AuthResponse(false, $"File size exceeds the limit of {MaxFileSize / (1024 * 1024)} MB.");

                var fileExtension = Path.GetExtension(file.FileName);
                if (string.IsNullOrEmpty(fileExtension) || !AllowedExtensions.Contains(fileExtension))
                    return new AuthResponse(false, $"Invalid file type. Allowed types: {string.Join(", ", AllowedExtensions)}");


                await using var memoryStream = new MemoryStream();
                await file.CopyToAsync(memoryStream, cancellationToken);

                var (isDocumentExist, errorMsg, contentHash) = await CheckIfDocumentExist(memoryStream, userId.Value, cancellationToken);

                if (isDocumentExist)
                {
                    return new AuthResponse(false, errorMsg);
                }
                #endregion

                #region Azure Blob Upload

                var (isUploadedToBlob, documentId, safeFileName, blobClient) = await UploadFileToBlob(userId, file, memoryStream, cancellationToken);
                if (!isUploadedToBlob)
                {
                    return new AuthResponse(false, "Failed to upload file to storage.");
                }
                uploadedBlobClient = blobClient;
                userDocumentId = documentId;
                #endregion Azure blob upload

                #region RAG Data Ingestion pipeline

                #region Step1: Text Extraction
                var extractedText = await _textExtractorService.ExtractTextAsync(memoryStream, fileExtension, cancellationToken);
                #endregion

                #region Step2: Text Chunking
                var chunks = _chunkingService.CreateChunks(text: extractedText, chunkSize: 1000, chunkOverlap: 200);
                
                _logger.LogInformation("Document {FileName} generated {ChunkCount} chunks.", safeFileName, chunks.Count);
                #endregion
                if (chunks.Count > 0)
                {
                    #region Step3: Embedding all chunks (768-dimensional vector). Default for gemini embedding model
                    var chunkTexts = chunks.Select(c => c.Content);
                    IReadOnlyList<ReadOnlyMemory<float>> vectors = await
                        _embeddingService.GenerateEmbeddingsAsync(chunkTexts, cancellationToken);
                    _logger.LogInformation("Generated {VectorCount} vectors for {FileName}", vectors.Count, safeFileName);
                    #endregion

                    #region Step4: Insert vectors to Azure AI Search
                    var searchChunks = new List<DocumentChunkModel>();
                    for (int i = 0; i < chunks.Count; i++)
                    {
                        searchChunks.Add(new DocumentChunkModel
                        {
                            Id = $"{documentId}_{i}",
                            DocumentId = documentId.ToString(),
                            UserId = userId.Value.ToString(),
                            FileName = safeFileName!,
                            Content = chunks[i].Content,
                            ChunkIndex = i,
                            ContentVector = vectors[i].ToArray()
                        });
                    }

                    await _azureSearchService.InsertChunksAsync(searchChunks, cancellationToken);

                    isChunksUploadedToAzureAI = true;
                    #endregion
                }

                #endregion Data Ingestion Pipeline

                #region Database update
                var userDocument = new UserDocument
                {
                    Id = documentId!.Value,
                    UserId = userId.Value,
                    FileName = safeFileName!,
                    BlobUrl = blobClient!.Uri.ToString(),
                    ChunkCount = chunks.Count,
                    UploadedAt = DateTime.UtcNow,
                    IsDeleted = false,
                    DeletedAt = null,
                    ContentHash = contentHash
                };

                _dbContext.UserDocuments.Add(userDocument);
                await _dbContext.SaveChangesAsync(cancellationToken);

                #endregion
                return new AuthResponse(true, Data: new
                {
                    blobName = $"users/{userId}/{documentId:N}_{safeFileName}",
                    originalFileName = safeFileName,
                    url = blobClient.Uri.ToString(),
                    size = file.Length,
                    uploadedAt = DateTime.UtcNow
                });
                
            }
            catch(Exception ex)
            {
                _logger.LogError(ex, "Failed to upload file for user {UserId}", userId);

                //Rollback 1: Delete chunks from Azure AI search if it is inserted
                if(isChunksUploadedToAzureAI && userDocumentId.HasValue)
                {
                    try
                    {
                        _logger.LogWarning("Rolling back: Deleting chunks from Azure AI Search for document {DocumentId}", userDocumentId.Value);
                        await _azureSearchService.DeleteChunksByDocumentIdAsync(userDocumentId.Value, cancellationToken);
                    }
                    catch(Exception azureSearchDeleteEx)
                    {
                        _logger.LogError(azureSearchDeleteEx, "Failed to delete Azure Search chunks during rollback for document {DocumentId}", userDocumentId.Value);
                    }
                }
                if(uploadedBlobClient != null && userDocumentId.HasValue)
                {
                    try
                    {
                        _logger.LogWarning("Rolling back: Deleting blob {BlobUri} from Azure Blob Storage", uploadedBlobClient.Uri);
                        await uploadedBlobClient.DeleteIfExistsAsync(DeleteSnapshotsOption.IncludeSnapshots, cancellationToken: cancellationToken);
                    }
                    catch (Exception blobEx)
                    {
                        _logger.LogError(blobEx, "Failed to delete blob during rollback: {BlobUri}", uploadedBlobClient.Uri);
                    }
                }
                return new AuthResponse(false, "Failed to upload file. Please try again.");
            }
        }

        public async Task<AuthResponse> Remove(Guid documentId, CancellationToken cancellationToken = default)
        {
            var userId = _currentUserService.UserId;
            try
            {
                if (userId == null) return new AuthResponse(false, "User is not authenticated.");

                var document = await _dbContext.UserDocuments
                              .FirstOrDefaultAsync(d => d.Id == documentId && d.UserId == userId.Value, cancellationToken);

                if (document == null)
                    return new AuthResponse(false, "Document not found.");

                document.IsDeleted = true;
                document.DeletedAt = DateTime.UtcNow;
                await _dbContext.SaveChangesAsync(cancellationToken);
                var uri = new Uri(document.BlobUrl);
                var blobName = Uri.UnescapeDataString(string.Concat(uri.Segments.Skip(2)));
                var blobClient = _containerClient.GetBlobClient(blobName);

                var response = await blobClient.DeleteIfExistsAsync(DeleteSnapshotsOption.IncludeSnapshots, cancellationToken: cancellationToken);
                if (!response.Value)
                {
                    _logger.LogWarning("Blob {BlobName} was not found in Azure (already deleted).", blobName);
                    return new AuthResponse(false, Data: new { message = "Failed to delete file. Please try again." });
                }

                return new AuthResponse(true, Data: new { message = "Document deleted successfully." });
            }
            catch(Exception ex)
            {
                _logger.LogError(ex, "Failed to remove document {DocumentId} for user {UserId}", documentId, userId);
                return new AuthResponse(false, "Failed to delete document. Please try again.");
            }
        }

        public async Task<AuthResponse> GetAllFiles(CancellationToken cancellationToken = default)
        {
            var userId = _currentUserService.UserId;
            try
            {
                if (userId == null) return new AuthResponse(false, "User is not authenticated");
                var documents = await _dbContext.UserDocuments.AsNoTracking().Where(d => d.UserId == userId).OrderByDescending(d => d.UploadedAt).Select(d => new
                {
                    d.Id,
                    d.FileName,
                    d.BlobUrl,
                    d.ChunkCount,
                    d.UploadedAt
                }).ToListAsync(cancellationToken);

                return new AuthResponse(true, Data: documents);
            }
            catch(Exception ex) 
            {
                _logger.LogError(ex, "Failed to retrieve documents for user {UserId}", userId);
                return new AuthResponse(false, "Failed to retrieve documents. Please try again.");
            }
        
        
        }

        private async Task<(bool isUploaded, Guid? documentId, string? safeFileName, BlobClient? blobClient)> UploadFileToBlob(Guid? userId, IFormFile? file, MemoryStream stream, CancellationToken cancellationToken = default) 
        {
            stream.Position = 0;
            var safeFileName = Path.GetFileName(file!.FileName);
            var documentId = Guid.NewGuid();
            var blobName = $"users/{userId}/{documentId:N}_{safeFileName}";

            var blobClient = _containerClient.GetBlobClient(blobName);

            var uploadOptions = new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = file.ContentType },
                Metadata = new Dictionary<string, string>
            {
                { "userId", userId!.Value.ToString() },
                { "originalFileName", safeFileName }
            }
            };

            var azureResponse = await blobClient.UploadAsync(stream, uploadOptions, cancellationToken);
            if (azureResponse.GetRawResponse().Status != (int)HttpStatusCode.Created)
            {
                _logger.LogError("File upload failed in Azure Blob for user {UserId}", userId);
                return (isUploaded: false, documentId: Guid.Empty, safeFileName: string.Empty, blobClient: null);
            }

            _logger.LogError("File upload succeeded in Azure Blob for user {UserId}", userId);
            return (isUploaded: true, documentId: documentId, safeFileName: safeFileName, blobClient: blobClient);
        }

        private async Task<(bool isDocumentExist, string errorMessage, string? contentHash)> CheckIfDocumentExist(MemoryStream stream, Guid userId, CancellationToken cancellationToken = default)
        {
            stream.Position = 0;
            using var sha256 = SHA256.Create();
            var hashBytes = await sha256.ComputeHashAsync(stream, cancellationToken);
            var contentHash = Convert.ToHexString(hashBytes);

            var existingDocument = await _dbContext.UserDocuments.AsNoTracking().FirstOrDefaultAsync(d => d.UserId == userId && d.ContentHash == contentHash, cancellationToken);

            //Document is already available in DB.
            if(existingDocument != null)
            {
                _logger.LogWarning("Duplicate content detected for user {UserId}. Matches existing file: {ExistingFileName}",
                        userId, existingDocument.FileName);

                return (true, $"This file is identical in content to '{existingDocument.FileName}', which is already uploaded.", null);
               
            }
            return (false, string.Empty, contentHash);
        }
    }
}
