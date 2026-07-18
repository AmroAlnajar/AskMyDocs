namespace AskMyDocs.API.Services;

public class DocumentService(IWebHostEnvironment environment) : IDocumentService
{
	private const int ChunkSize = 500;
	private const int Overlap = 100;

	public async Task<List<DocumentChunk>> GetDocumentChunksAsync()
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
		var paragraphs = content
			.Split(
				["\r\n\r\n", "\n\n"],
				StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

		var currentChunk = new List<string>();
		var currentLength = 0;

		foreach (var paragraph in paragraphs)
		{
			if (currentLength + paragraph.Length > ChunkSize &&
				currentChunk.Count > 0)
			{
				yield return new DocumentChunk(
					string.Join("\n\n", currentChunk),
					source);

				var overlapParagraphs = currentChunk
					.TakeLast(1)
					.ToList();

				currentChunk = overlapParagraphs;
				currentLength = overlapParagraphs.Sum(p => p.Length);
			}

			currentChunk.Add(paragraph);
			currentLength += paragraph.Length;
		}

		if (currentChunk.Count > 0)
		{
			yield return new DocumentChunk(
				string.Join("\n\n", currentChunk),
				source);
		}
	}
}