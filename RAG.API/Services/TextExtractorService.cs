using System.Text;
using UglyToad.PdfPig;

namespace RAG.API.Services
{
    public interface ITextExtractorService
    {
        /// <summary>
        /// Extracts clean text content from an uploaded document stream based on its extension.
        /// </summary>
        /// <param name="stream"></param>
        /// <param name="fileExtension"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        Task<string> ExtractTextAsync(Stream stream, string fileExtension, CancellationToken cancellationToken = default);
    }

    public class TextExtractorService : ITextExtractorService
    {
        private readonly ILogger<TextExtractorService> _logger;

        public TextExtractorService(ILogger<TextExtractorService> logger)
        {
            _logger = logger;
        }

        public async Task<string> ExtractTextAsync(Stream stream, string fileExtension, CancellationToken cancellationToken = default)
        {
            if (stream.CanSeek)
            {
                stream.Position = 0;
            }

            var extension = fileExtension.ToLowerInvariant();

            return extension switch
            {
                ".pdf" => ExtractFromPdf(stream),
                ".txt" or ".md" or ".csv" => await ExtractFromPlainTextAsync(stream, cancellationToken),
                _ => throw new NotSupportedException($"File extension '{fileExtension}' is not supported for text extraction.")
            };
        }

        private string ExtractFromPdf(Stream stream)
        {
            try
            {
                var sb = new StringBuilder();

                using var document = PdfDocument.Open(stream);
                foreach (var page in document.GetPages())
                {
                    if (string.IsNullOrWhiteSpace(page.Text))
                        continue;

                    sb.AppendLine(page.Text);
                }

                return sb.ToString().Trim();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to extract text from PDF document.");
                throw new InvalidOperationException("Failed to read text from the uploaded PDF. The file may be corrupt or password-protected.", ex);
            }
        }

        private static async Task<string> ExtractFromPlainTextAsync(Stream stream, CancellationToken cancellationToken)
        {
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
            var content = await reader.ReadToEndAsync(cancellationToken);
            return content.Trim();
        }
    }
}

