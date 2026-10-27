using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RAG.API.Services;

namespace RAG.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class FileUploadController : ControllerBase
    {
        
        private readonly IFileService _fileService;

        public FileUploadController(IFileService fileService)
        {
            _fileService = fileService;
        }

        [HttpPost("Upload")]
        [Authorize]
        public async Task<IActionResult> UploadAsync(IFormFile? file, CancellationToken cancellationToken)
        {
            //1. Validate the file presence
            if (file == null || file.Length == 0) 
                return BadRequest("No file was uploaded or file is empty.");

            var response = await _fileService.Upload(file, cancellationToken);

            if(!response.Success) return BadRequest(new { message = response.ErrorMessage });

            return Ok(response.Data);
        }

        [HttpPost("Remove/{documentId:guid}")]
        [Authorize]
        public async Task<IActionResult>RemoveAsync(Guid documentId, CancellationToken cancellationToken)
        {
            
            if (documentId == Guid.Empty) return BadRequest("document Id is required");

            var response = await _fileService.Remove(documentId, cancellationToken);

            if (!response.Success) return BadRequest(new { message = response.ErrorMessage });

            return Ok(response.Data);
        }

        [HttpGet("GetAll")]
        [Authorize]
        public async Task<IActionResult> GetAllDocuments(CancellationToken cancellationToken)
        {
            var response = await _fileService.GetAllFiles(cancellationToken);

            if (!response.Success) return BadRequest(new { message = response.ErrorMessage });

            return Ok(response.Data);
        }
    }
}
