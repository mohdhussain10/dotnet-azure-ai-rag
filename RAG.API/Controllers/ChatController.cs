using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RAG.API.DTOs;
using RAG.API.Services;

namespace RAG.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class ChatController : ControllerBase
    {
        private readonly IchatService _chatService;
        private readonly ILogger<ChatController> _logger;

        public ChatController(IchatService chatService, ILogger<ChatController> logger)
        {
            _chatService = chatService;
            _logger = logger;
        }

        /// <summary>
        /// Send a message, perform RAG retrieval, and get a grounded answer.
        /// </summary>
        /// <param name="request"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        [HttpPost]
        public async Task<IActionResult> Ask([FromBody] ChatRequest request, CancellationToken cancellationToken)
        {
            if(string.IsNullOrEmpty(request.Message))
                return BadRequest(new { message = "Message is required." });

            var response = await _chatService.AskAsync(request, cancellationToken);
            if (!response.Success)
            {
                return BadRequest(new { message = response.ErrorMessage });
            }
            return Ok(response.Data);
        }

        /// <summary>
        /// Get conversations specific to user
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        [HttpGet("conversations")]
        public async Task<IActionResult> GetConversations(CancellationToken cancellationToken)
        {
            var response = await _chatService.GetUserConversationsAsync(cancellationToken);
            if (!response.Success)
                return BadRequest(new { message = response.ErrorMessage });
            return Ok(response.Data);
        }

        /// <summary>
        /// Get full chat history for a single conversation.
        /// </summary>
        [HttpGet("conversations/{conversationId:guid}")]
        public async Task<IActionResult> GetConversationMessages(Guid conversationId, CancellationToken cancellationToken)
        {
            var response = await _chatService.GetConversationMessagesAsync(conversationId, cancellationToken);
            if (!response.Success)
                return NotFound(new { message = response.ErrorMessage });
            return Ok(response.Data);
        }

        /// <summary>
        /// Delete a conversation.
        /// </summary>
        [HttpDelete("conversations/{conversationId:guid}")]
        public async Task<IActionResult> DeleteConversation(Guid conversationId, CancellationToken cancellationToken)
        {
            var response = await _chatService.DeleteConversationAsync(conversationId, cancellationToken);
            if (!response.Success)
                return BadRequest(new { message = response.ErrorMessage });
            return Ok(response.Data);
        }


    }
}


