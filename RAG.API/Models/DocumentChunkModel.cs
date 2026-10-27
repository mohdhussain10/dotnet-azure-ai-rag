using Azure.Search.Documents.Indexes;

namespace RAG.API.Models
{
    /// <summary>
    /// Schema of Azure AI Search documents
    /// </summary>
    public class DocumentChunkModel
    {
        /// <summary>
        /// Id: Unique key
        /// </summary>
        [SimpleField(IsKey = true, IsFilterable = true)]
        public string Id { get; set; } = string.Empty;

        [SimpleField(IsFilterable = true)]
        public string DocumentId { get; set; } = string.Empty;

        [SimpleField(IsFilterable = true)]
        public string UserId { get; set; } = string.Empty;

        [SimpleField(IsFilterable = true)]
        public string FileName { get; set; } = string.Empty;

        [SearchableField]
        public string Content { get; set; } = string.Empty;

        [SimpleField(IsSortable = true)] //Text can be searched via hybrid search
        public int ChunkIndex {  get; set; }

        [VectorSearchField(VectorSearchDimensions = 768, VectorSearchProfileName = "gemini-vector-profile")]
        public float[] ContentVector { get; set; } = Array.Empty<float>();
    }
}
