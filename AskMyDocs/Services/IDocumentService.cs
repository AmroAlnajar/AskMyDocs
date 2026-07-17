namespace askmydocs.Services;

public interface IDocumentService
{
	Task<List<DocumentChunk>> GetDocumentChunksAsync();
}

public record DocumentChunk(
	string Content,
	string Source);