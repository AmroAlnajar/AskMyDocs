namespace AskMyDocs.API.Services.Documents;

public interface IDocumentService
{
	Task<List<DocumentChunk>> GetDocumentChunksAsync(CancellationToken cancellationToken = default);
}

public record DocumentChunk(
	string Content,
	string Source);