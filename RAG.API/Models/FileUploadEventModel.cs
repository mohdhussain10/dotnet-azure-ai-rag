namespace RAG.API.Models
{
    public class FileUploadEventModel
    {
        public Guid DocumentId { get; set; }

        public Guid UserId { get; set; }

        public string FileName {  get; set; }

        public string BlobUrl { get; set; }

        public string FileExtension { get; set; }
    }
}
