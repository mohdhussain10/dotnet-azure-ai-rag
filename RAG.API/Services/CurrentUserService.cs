using System.Security.Claims;

namespace RAG.API.Services
{
    public interface ICurrentUserService
    {
        Guid? UserId { get; }
        string? Email { get; }
        bool IsAuthenticated { get; }
    }
    public class CurrentUserService : ICurrentUserService
    {
        private readonly IHttpContextAccessor _httpContextAccessor;

        public CurrentUserService(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        public Guid? UserId
        {
            get
            {
                var user = _httpContextAccessor.HttpContext?.User;
                var id = user?.FindFirstValue(ClaimTypes.NameIdentifier) ?? user?.FindFirstValue("sub");
                return Guid.TryParse(id, out var guid) ? guid : null;
            }
        }

        public string? Email =>
          _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.Email)
          ?? _httpContextAccessor.HttpContext?.User?.FindFirstValue("email");


        public bool IsAuthenticated =>
         _httpContextAccessor.HttpContext?.User?.Identity?.IsAuthenticated ?? false;
    }
}
