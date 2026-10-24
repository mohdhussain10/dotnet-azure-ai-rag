using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RAG.API.DTOs;
using RAG.API.Services;
using RAG.Infrastructure.Data;

namespace RAG.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthenticationController : ControllerBase
    {
        private readonly IHttpClientFactory _httpClientFactory;

        private readonly IConfiguration _config;

        private readonly AppDbContext _dbContext;

        private readonly IAuthService _authService;

        public AuthenticationController(IHttpClientFactory httpClientFactory, IConfiguration config, AppDbContext dbContext, IAuthService authService)
        {
            _httpClientFactory = httpClientFactory;
            _config = config;
            _dbContext = dbContext;
            _authService = authService;
        }

        #region Public Methods

        /// <summary>
        /// To register user
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        [HttpPost("signup")]
        [AllowAnonymous]
        public async Task<IActionResult> SignUp([FromBody] UserRegistration request, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(request.Email) || string.IsNullOrEmpty(request.Password))
                return BadRequest("Email and Password are required");

            var response = await _authService.SignUpAsync(request, cancellationToken);

            if (!response.Success && !string.IsNullOrEmpty(response.ErrorMessage))
            {
                var error = response.ErrorMessage;
                
                if(error.Contains("User already registered", StringComparison.OrdinalIgnoreCase))
                {
                    return BadRequest(new {message = "An account with this email already exists." });
                }
                else if(error.Contains("Password should be at least", StringComparison.OrdinalIgnoreCase))
                {
                    return BadRequest(new { message = "Password does not meet complexity requirements."});
                }
                else
                {
                    return BadRequest(new { message = "Unable to register account. Please check your details and try again." });
                }
            }

            return Ok(response.Data);
        }

        [HttpPost("Login")]
        [AllowAnonymous]
        public async Task<IActionResult> LogIn([FromBody] UserLogin request, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(request.Email) || string.IsNullOrEmpty(request.Password))
                return BadRequest("Email and Password are required");

            var response = await _authService.LoginAsync(request, cancellationToken);

            if (!response.Success)
            {
                var error = response.ErrorMessage ?? string.Empty;
                // 1. Invalid email or password
                if (error.Contains("invalid_grant", StringComparison.OrdinalIgnoreCase) ||
                    error.Contains("Invalid login credentials", StringComparison.OrdinalIgnoreCase))
                {
                    return Unauthorized(new { message = "Invalid email or password." });
                }
                if (error.Contains("rate_limit", StringComparison.OrdinalIgnoreCase) ||
                    error.Contains("Too Many Requests", StringComparison.OrdinalIgnoreCase))
                {
                    return StatusCode(429, new { message = "Too many login attempts. Please wait a few minutes and try again." });
                }

                return BadRequest(new { message = "Unable to sign in. Please check your credentials and try again." });
            }

            return Ok(response.Data);

        } 
        #endregion
    }
}
