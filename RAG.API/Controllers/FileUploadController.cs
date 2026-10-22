using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics.Eventing.Reader;

namespace RAG.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class FileUploadController : ControllerBase
    {
        private readonly IConfiguration _config;

        private readonly BlobContainerClient _containerClient;

        /// <summary>
        /// To check Allowed Document Extensions
        /// </summary>
        private static HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase) { ".pdf", ".docx", ".txt", ".md", ".csv" };

        /// <summary>
        /// To check file max size
        /// </summary>
        private const long MaxFileSize = 25 * 1024 * 1024;
        public FileUploadController(IConfiguration config, BlobContainerClient containerClient)
        {
            _config = config;
            _containerClient = containerClient;
        }

        [HttpPost]
        public async Task<IActionResult> Upload(IFormFile? file, CancellationToken cancellationToken)
        {
            //1. Validate the file presence
            if (file == null || file.Length == 0) 
                return BadRequest("No file was uploaded or file is empty.");

            //2. Validate file size
            if(file.Length > MaxFileSize)
                return BadRequest($"File size exceeds the limit of {MaxFileSize / (1024 * 1024)} MB.");

            //3. Validate file extension
            var fileExtension = Path.GetExtension(file.FileName);
            if(string.IsNullOrEmpty(fileExtension) || !AllowedExtensions.Contains(fileExtension))
                return BadRequest($"Invalid file type. Allowed types: {string.Join(", ", AllowedExtensions)}");

            //4. Sanitize the file name to prevent path traversal
            var safeFileName = Path.GetFileName(file.FileName);
            var userId = "123"; // Will remove later
            var blobName = $"users/{userId}/{Guid.NewGuid():N}_{safeFileName}";
            var blobClient = _containerClient.GetBlobClient(blobName);

            var uploadOptions = new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = file.ContentType },
                // Store userId in metadata so Azure Search Indexer can read and index it
                Metadata = new Dictionary<string, string>
                {
                    { "userId", userId }
                }
            };
            await using var stream = file.OpenReadStream();
            await blobClient.UploadAsync(stream, uploadOptions, cancellationToken);
            return Ok(new { blobName, originalName = safeFileName });
        }
    }
}
