namespace AskMyDocs.API.Services.Documents;

public class DocumentService(IWebHostEnvironment environment) : IDocumentService
{
	public async Task<List<DocumentChunk>> GetDocumentChunksAsync(CancellationToken cancellationToken = default)
	{
		var knowledgeBasePath = Path.Combine(
			environment.ContentRootPath,
			"Knowledgebase");

		var files = Directory.GetFiles(
			knowledgeBasePath,
			"*.md");

		var chunks = new List<DocumentChunk>();

		foreach (var file in files)
		{
			var content = await File.ReadAllTextAsync(file, cancellationToken);

			chunks.AddRange(
				MarkdownChunker.Split(
					content,
					Path.GetFileName(file)));
		}

		return chunks;
	}
}
