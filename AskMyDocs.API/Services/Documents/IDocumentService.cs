namespace AskMyDocs.API.Services.Documents;

public interface IDocumentService
{
	Task<List<DocumentChunk>> GetDocumentChunksAsync();
}

public record DocumentChunk(
	string Content,
	string Source);