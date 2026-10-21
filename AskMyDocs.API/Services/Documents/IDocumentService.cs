namespace AskMyDocs.API.Services.Documents;

public interface IDocumentService
{
	Task<List<DocumentChunk>> GetDocumentChunksAsync(CancellationToken cancellationToken = default);
}

public record DocumentChunk(
	string Content,
	string Source,
	string Heading = "",
	int Index = 0)
{
	public string EmbeddingText =>
		string.IsNullOrEmpty(Heading)
			? $"[{Source}]\n{Content}"
			: $"[{Source}] {Heading}\n{Content}";
}