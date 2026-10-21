using AskMyDocs.API.Services.Documents;

namespace AskMyDocs.Tests.Services;

public class MarkdownChunkerTests
{
	[Fact]
	public void Split_WhenContentIsWhitespace_ReturnsNoChunks()
	{
		Assert.Empty(MarkdownChunker.Split("  \n\n\t", "blank.md"));
	}

	[Fact]
	public void Split_WhenNoHeadings_StoresEmptyHeadingAndSourcePrefixOnEmbedding()
	{
		var chunks = MarkdownChunker.Split("Helix uses a modular monolith.", "architecture.md");

		var chunk = Assert.Single(chunks);
		Assert.Equal("", chunk.Heading);
		Assert.Equal("[architecture.md]\nHelix uses a modular monolith.", chunk.EmbeddingText);
	}

	[Fact]
	public void Split_WhenSectionExceedsChunkSize_KeepsHeadingOnEveryPiece()
	{
		var body = string.Join("\n\n", Enumerable.Range(0, 4).Select(i => new string((char)('a' + i), 200)));
		var chunks = MarkdownChunker.Split($"## Tokens\n\n{body}", "authentication.md");

		Assert.True(chunks.Count >= 2);
		Assert.All(chunks, chunk => Assert.Equal("Tokens", chunk.Heading));
		Assert.All(chunks, chunk => Assert.Equal("authentication.md", chunk.Source));
	}

	[Fact]
	public void Split_WhenTableIsLong_SplitsOnRows()
	{
		var header = "| Role | Group |\n| --- | --- |";
		var rows = string.Join("\n", Enumerable.Range(0, 20).Select(i => $"| Helix.Role{i} | grp-helix-{i} |"));
		var chunks = MarkdownChunker.Split($"## Roles\n\n{header}\n{rows}", "authentication.md");

		Assert.True(chunks.Count >= 2);
		Assert.All(chunks, chunk => Assert.True(chunk.Content.Length <= MarkdownChunker.ChunkSize + MarkdownChunker.Overlap));
		Assert.Contains(chunks, chunk => chunk.Content.Contains("Helix.Role0"));
		Assert.Contains(chunks, chunk => chunk.Content.Contains("Helix.Role19"));
	}
}
