using FluentValidation;

namespace RAG.API.Validator
{
    public class FileUploadValidator : AbstractValidator<IFormFile>
    {
        private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".pdf", ".docx", ".txt", ".md", ".csv"
        };

        private const long MaxFileSizeBytes = 25 * 1024 * 1024;

        public FileUploadValidator()
        {
            RuleFor(file => file)
               .NotNull()
               .WithMessage("No file was uploaded.");
            When(file => file != null, () =>
            {
                RuleFor(file => file!.Length).GreaterThan(0).WithMessage("Uploaded file is empty");

                RuleFor(file => file!.Length).LessThanOrEqualTo(MaxFileSizeBytes).WithMessage($"File size exceeds the limit of{MaxFileSizeBytes / (1024 * 1024)} MB.");

                RuleFor(file => Path.GetExtension(file.FileName)).NotEmpty().WithMessage("File must have an extension.")
                .Must(ext => AllowedExtensions.Contains(ext)).WithMessage($"Invalid file type. Allowed extensions: {string.Join(", ", AllowedExtensions)}");
            });
        }
    }
}
