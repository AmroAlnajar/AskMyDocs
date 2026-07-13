namespace askmydocs.Services;

public class DocumentService(IWebHostEnvironment environment) : IDocumentService
{
	private const int ChunkSize = 500;
	private const int Overlap = 100;

	public async Task<List<DocumentChunk>> GetDocumentChunksAsync()
	{
		var knowledgeBasePath = Path.Combine(
			environment.ContentRootPath,
			"KnowledgeBase");

		var files = Directory.GetFiles(
			knowledgeBasePath,
			"*.md");

		var chunks = new List<DocumentChunk>();

		foreach (var file in files)
		{
			var content = await File.ReadAllTextAsync(file);

			chunks.AddRange(
				SplitIntoChunks(
					content,
					Path.GetFileName(file)));
		}

		return chunks;
	}

	private static IEnumerable<DocumentChunk> SplitIntoChunks(
		string content,
		string source)
	{
		var start = 0;

		while (start < content.Length)
		{
			var length = Math.Min(
				ChunkSize,
				content.Length - start);

			var chunk = content.Substring(start, length);

			yield return new DocumentChunk(chunk, source);

			start += ChunkSize - Overlap;
		}
	}
}