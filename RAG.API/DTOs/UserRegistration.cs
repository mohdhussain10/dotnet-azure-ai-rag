using Azure.Storage.Blobs.Models;

namespace RAG.API.DTOs
{
    /// <summary>
    /// To Register user in the application
    /// </summary>
    /// <param name="Email"></param>
    /// <param name="Password"></param>
    /// <param name="FirstName"></param>
    /// <param name="LastName"></param>
    public record UserRegistration(string Email, string Password, string FirstName, string? LastName)
    {
        public string GetFullName() => string.IsNullOrWhiteSpace(LastName) ? FirstName : $"{FirstName} {LastName}".Trim();
    };
}
