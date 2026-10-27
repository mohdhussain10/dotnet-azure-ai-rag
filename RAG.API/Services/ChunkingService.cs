using RAG.API.Models;

namespace RAG.API.Services
{
    public interface IChunkingService
    {
        /// <summary>
        /// Splits a document's full text into overlapping chunks.
        /// </summary>
        /// <param name="text"></param>
        /// <param name="chunkSize">Target chunk character length (1,000 characters ~ 200-250 words).</param>
        /// <param name="chunkOverlap">Overlap between consecutive chunks (Default: 200 characters). Explanation is in md file</param>
        List<TextChunk> CreateChunks(string text, int chunkSize = 1000, int chunkOverlap = 200);
    }

    public class ChunkingService : IChunkingService
    {
        /// <summary>
        /// Splits a document's full text into overlapping chunks.
        /// </summary>
        /// <param name="text"></param>
        /// <param name="chunkSize"></param>
        /// <param name="chunkOverlap"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentException"></exception>
        public List<TextChunk> CreateChunks(string text, int chunkSize = 1000, int chunkOverlap = 200)
        {
            var chunks = new List<TextChunk>();

            if (string.IsNullOrEmpty(text)) return chunks;

            if(chunkOverlap >= chunkSize) throw new ArgumentException("Chunk overlap must be smaller than chunk size.");

            text = text.Trim();

            //docuement is small enough to fit in single chunk
            if(text.Length <= chunkSize)
            {
                chunks.Add(new TextChunk(0, text, 0, text.Length));
                return chunks;
            }
            int startIndex = 0;
            int chunkIndex = 0;

            while(startIndex < text.Length)
            {
                int length = Math.Min(chunkSize, text.Length - startIndex);

                if (startIndex + length < text.Length)
                {
                    int lastSpace = text.LastIndexOf(' ', startIndex + length, Math.Min(length, 100));
                    if (lastSpace > startIndex)
                    {
                        length = lastSpace - startIndex;
                    }
                }
                var chunkContent = text.Substring(startIndex, length).Trim();
                if (!string.IsNullOrWhiteSpace(chunkContent))
                {
                    chunks.Add(new TextChunk(chunkIndex++, chunkContent, startIndex, startIndex + length));
                }
                
                startIndex += Math.Max(1, length - chunkOverlap);
            }
            return chunks;
        }

        

    }
}
