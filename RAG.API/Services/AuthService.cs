using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RAG.API.DTOs;
using RAG.API.Models;
using RAG.Domain.Entities;
using RAG.Infrastructure.Data;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace RAG.API.Services
{
    public interface IAuthService
    {
        /// <summary>
        /// To register user
        /// </summary>
        /// <param name="request"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        Task<AuthResponse> SignUpAsync(UserRegistration request, CancellationToken cancellationToken = default);
      
        /// <summary>
        /// To login user
        /// </summary>
        /// <param name="request"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        Task<AuthResponse> LoginAsync(UserLogin request, CancellationToken cancellationToken = default);
    }
    public class AuthService : IAuthService
    {
        private readonly HttpClient _httpClient;

        private readonly AppDbContext _dbContext;

        private readonly ILogger<AuthService> _logger;

        private readonly SupabaseOptions _supabaseOptions;

        private readonly IConfiguration _config;

        public AuthService(HttpClient httpClient, AppDbContext dbContext, ILogger<AuthService> logger, IOptions<SupabaseOptions> supabaseOptions, IConfiguration config  )
        {
            _httpClient = httpClient;
            _dbContext = dbContext;
            _logger = logger;
            _supabaseOptions = supabaseOptions.Value;
            _config = config;
        }

        /// <summary>
        /// To Register user
        /// </summary>
        /// <param name="request"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<AuthResponse> SignUpAsync(UserRegistration request, CancellationToken cancellationToken = default)
        {
            try
            {
                var payload = new
                {
                    email = request.Email,
                    password = request.Password,
                    data = new { full_name = request.GetFullName() }
                };

                _httpClient.DefaultRequestHeaders.Add("apikey", _supabaseOptions.Key);
                var response = await _httpClient.PostAsJsonAsync(_supabaseOptions.BuildAuthUrl("signup"), payload, cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    var errorJson = await response.Content.ReadAsStringAsync(cancellationToken);
                    return new AuthResponse(false, errorJson);
                }

                var authResult = await response.Content.ReadFromJsonAsync<SupabaseAuthResponse>(cancellationToken) ?? new();

                if(authResult.User == null || authResult.User.Id == Guid.Empty)
                {
                    return new AuthResponse(false, "An unexpected error occurred during registration. Please try again.");
                }
                var userId = authResult.User.Id;
                var profile = new Profile
                {
                    Id = userId,
                    Email = request.Email,
                    FullName = request.GetFullName(),
                    CreatedAt = DateTime.UtcNow
                };

                _dbContext.Profiles.Add(profile);
                await _dbContext.SaveChangesAsync(cancellationToken);

                return new AuthResponse(true, Data: new
                {
                    userId,
                    email = profile.Email,
                    fullName = profile.FullName,
                    accessToken = authResult.AccessToken
                });
                
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error occurred during SignUp for {Email}", request.Email);
                return new AuthResponse(false, "Something went wrong on our end. Please try again later.");
            }
        }


        /// <summary>
        /// To Login user
        /// </summary>
        /// <param name="request"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<AuthResponse> LoginAsync(UserLogin request, CancellationToken cancellationToken = default)
        {
            try
            {
                var payload = new
                {
                    email = request.Email,
                    password = request.Password,
                };

                if (!_httpClient.DefaultRequestHeaders.Contains("apikey"))
                {
                    _httpClient.DefaultRequestHeaders.Add("apikey", _supabaseOptions.Key);
                }
            
                var response = await _httpClient.PostAsJsonAsync(_supabaseOptions.BuildAuthUrl("token?grant_type=password"), payload, cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    var errorJson = await response.Content.ReadAsStringAsync(cancellationToken);
                    _logger.LogWarning("Supabase login failed for {Email}. Response: {Error}", request.Email, errorJson);
                    return new AuthResponse(false, errorJson);
                }
                var authResult = await response.Content.ReadFromJsonAsync<SupabaseAuthResponse>(cancellationToken) ?? new();
                return new AuthResponse(true, Data: new
                {
                    Id = authResult?.User?.Id,
                    FullName = authResult?.User?.UserMetadata?.FullName,
                    email = authResult?.User?.Email,
                    accessToken = authResult?.AccessToken
                });
            }
            catch (Exception ex) 
            {
                _logger.LogError(ex, "Unexpected error occurred during Login for {Email}", request.Email);
                return new AuthResponse(false, "Something went wrong on our end. Please try again later.");
            }
        }
    }
}
