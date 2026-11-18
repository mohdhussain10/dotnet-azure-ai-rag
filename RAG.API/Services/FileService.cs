using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.EntityFrameworkCore;
using RAG.API.Models;
using RAG.Domain.Entities;
using RAG.Infrastructure.Data;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;

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
        private readonly BlobContainerClient _containerClient;
        private readonly ILogger<FileService> _logger;
        private readonly ICurrentUserService _currentUserService;
        private readonly AppDbContext _dbContext;

        public FileService(
            BlobContainerClient containerClient,
            ILogger<FileService> logger,
            ICurrentUserService currentUserService,
            AppDbContext dbContext)
        {
            _containerClient = containerClient;
            _logger = logger;
            _currentUserService = currentUserService;
            _dbContext = dbContext;
        }
        public async Task<AuthResponse> Upload(IFormFile? file, CancellationToken cancellationToken = default)
        {
            var userId = _currentUserService.UserId;
            Guid? userDocumentId = null;
           
            try
            {
                #region Prequisites checks and variables declaration
                if (userId == null) return new AuthResponse(false, "User is not authenticated.");
                
                var fileExtension = Path.GetExtension(file.FileName);
                
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
                userDocumentId = documentId;
                #endregion Azure blob upload

                #region Write Document to DB + writing event to outbox event table
                await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
                try
                {
                    var userDocument = new UserDocument
                    {
                        Id = documentId!.Value,
                        UserId = userId.Value,
                        FileName = safeFileName!,
                        BlobUrl = blobClient!.Uri.ToString(),
                        ChunkCount = 0,
                        UploadedAt = DateTime.UtcNow,
                        IsDeleted = false,
                        DeletedAt = null,
                        ContentHash = contentHash,
                        Status = DocumentStatus.Pending
                    };

                    _dbContext.UserDocuments.Add(userDocument);

                    var eventPayload = JsonSerializer.Serialize(new FileUploadEventModel
                    {
                        DocumentId = userDocument.Id,
                        UserId = userDocument.UserId,
                        FileName = userDocument.FileName,
                        BlobUrl = userDocument.BlobUrl,
                        FileExtension = Path.GetExtension(file.FileName)
                    });

                    var outboxEvent = new OutboxEvent
                    {
                        AggregateId = userDocument.Id,
                        EventType = "FileUploaded",
                        PayloadJson = eventPayload,
                        CreatedAt = DateTime.UtcNow
                    };

                    _dbContext.OutboxEvents.Add(outboxEvent);

                    await _dbContext.SaveChangesAsync(cancellationToken);

                    await transaction.CommitAsync(cancellationToken);

                    return new AuthResponse(true, Data: new
                    {
                        documentId = userDocument.Id,
                        fileName = safeFileName,
                        status = DocumentStatus.Pending.ToString(),
                        message = "File uploaded successfully. Processing started in background."
                    });
                }
                catch(Exception ex)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    _logger.LogError(ex, "Transaction failed for user document {DocumentId}", documentId);
                    return new AuthResponse(false, "Failed to initiate file processing.");
                }
                #endregion
            }
            catch(Exception ex)
            {
                _logger.LogError(ex, "Failed to upload file for user {UserId}", userId);
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
