namespace RAG.API.Models
{
    public record TextChunk(
        //Specifies chunk order like 0,1,2,3,...
        int Order, 
        //content of this chunk
        string Content,
        //chunk first character index
        int StartIndex,
        //chunk last character index
        int EndIndex
        );
  
}
