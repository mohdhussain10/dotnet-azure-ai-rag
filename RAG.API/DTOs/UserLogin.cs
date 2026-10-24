namespace RAG.API.DTOs
{
    /// <summary>
    /// To login user to the application
    /// </summary>
    /// <param name="Email"></param>
    /// <param name="Password"></param>
    public record UserLogin(string Email, string Password);
    
}
