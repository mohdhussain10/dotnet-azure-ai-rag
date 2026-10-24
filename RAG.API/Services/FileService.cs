using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.EntityFrameworkCore;
using RAG.API.DTOs;
using RAG.API.Models;
using RAG.Domain.Entities;
using RAG.Infrastructure.Data;
using System.Diagnostics.CodeAnalysis;
using System.Reflection.Metadata;

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


        public FileService(BlobContainerClient containerClient, ILogger<FileService> logger, ICurrentUserService currentUserService, AppDbContext dbContext)
        {
            _containerClient = containerClient;
            _logger = logger;
            _currentUserService = currentUserService;
            _dbContext = dbContext;
        }
        public async Task<AuthResponse> Upload(IFormFile? file, CancellationToken cancellationToken = default)
        {
            var userId = _currentUserService.UserId;
            try
            {
                if(userId == null) return new AuthResponse(false, "User is not authenticated.");

                if (file!.Length > MaxFileSize)
                    return new AuthResponse(false, $"File size exceeds the limit of {MaxFileSize / (1024 * 1024)} MB.");

                var fileExtension = Path.GetExtension(file.FileName);
                if (string.IsNullOrEmpty(fileExtension) || !AllowedExtensions.Contains(fileExtension))
                    return new AuthResponse(false, $"Invalid file type. Allowed types: {string.Join(", ", AllowedExtensions)}");

                var safeFileName = Path.GetFileName(file.FileName);
                var blobName = $"users/{userId}/{Guid.NewGuid():N}_{safeFileName}";
                var blobClient = _containerClient.GetBlobClient(blobName);

                var uploadOptions = new BlobUploadOptions
                {
                    HttpHeaders = new BlobHttpHeaders { ContentType = file.ContentType },
                    // Store userId in metadata so Azure Search Indexer can read and index it
                    Metadata = new Dictionary<string, string>
                {
                    { "userId", userId.Value.ToString()},
                    { "originalFileName", safeFileName }
                }
                };
                await using var stream = file.OpenReadStream();
                var azureResponse = await blobClient.UploadAsync(stream, uploadOptions, cancellationToken);

                var userDocument = new UserDocument
                {
                    Id = Guid.NewGuid(),
                    UserId = userId.Value,
                    FileName = safeFileName,
                    BlobUrl = blobClient.Uri.ToString(),
                    ChunkCount = 0,
                    UploadedAt = DateTime.UtcNow,
                    IsDeleted = false,
                    DeletedAt = null
                };

                _dbContext.UserDocuments.Add(userDocument);
                await _dbContext.SaveChangesAsync(cancellationToken);


                return new AuthResponse(true, Data: new
                {
                    blobName,
                    originalFileName = safeFileName,
                    url = blobClient.Uri.ToString(),
                    size = file.Length,
                    uploadedAt = azureResponse.Value.LastModified
                });
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

                var uri = new Uri(document.BlobUrl);
                var blobName = string.Join("/", uri.Segments.Skip(2)).TrimStart('/');
                var blobClient = _containerClient.GetBlobClient(blobName);

                _ = await blobClient.DeleteIfExistsAsync(DeleteSnapshotsOption.IncludeSnapshots, cancellationToken: cancellationToken);

                document.IsDeleted = true;
                document.DeletedAt = DateTime.UtcNow;

                await _dbContext.SaveChangesAsync(cancellationToken);

                return new AuthResponse(true, Data: new { message = "Document removed successfully." });
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



        }
}
