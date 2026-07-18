using AskMyDocs.API.Services.Documents;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;

namespace AskMyDocs.Tests.Services;

public class DocumentServiceTests : IDisposable
{
    // Future reference: Must match DocumentService.ChunkSize.
    private const int ChunkSize = 500;

    private readonly string _contentRoot;
    private readonly string _knowledgeBasePath;

    public DocumentServiceTests()
    {
        _contentRoot = Path.Combine(
            Path.GetTempPath(),
            "AskMyDocs.DocumentServiceTests",
            Guid.NewGuid().ToString("N"));

        _knowledgeBasePath = Path.Combine(_contentRoot, "Knowledgebase");
        Directory.CreateDirectory(_knowledgeBasePath);
    }

    public void Dispose()
    {
        if (Directory.Exists(_contentRoot))
        {
            Directory.Delete(_contentRoot, recursive: true);
        }
    }

    [Fact]
    public async Task GetDocumentChunksAsync_WhenFileIsShort_ReturnsSingleChunkWithSourceFilename()
    {
        WriteMarkdown("architecture.md", "Helix uses a modular monolith.");

        var chunks = await CreateSut().GetDocumentChunksAsync();

        var chunk = Assert.Single(chunks);
        Assert.Equal("architecture.md", chunk.Source);
        Assert.Equal("Helix uses a modular monolith.", chunk.Content);
    }

    [Fact]
    public async Task GetDocumentChunksAsync_WhenParagraphsFitInChunkSize_ReturnsSingleChunk()
    {
        var first = Paragraph(200, 'a');
        var second = Paragraph(200, 'b');
        WriteMarkdown("guide.md", JoinParagraphs(first, second));

        var chunks = await CreateSut().GetDocumentChunksAsync();

        var chunk = Assert.Single(chunks);
        Assert.Equal(JoinParagraphs(first, second), chunk.Content);
        Assert.Equal("guide.md", chunk.Source);
    }

    [Fact]
    public async Task GetDocumentChunksAsync_WhenParagraphsExceedChunkSize_SplitsIntoMultipleChunks()
    {
        var first = Paragraph(400, 'a');
        var second = Paragraph(150, 'b');
        var third = Paragraph(150, 'c');
        WriteMarkdown("guide.md", JoinParagraphs(first, second, third));

        var chunks = await CreateSut().GetDocumentChunksAsync();

        Assert.Equal(3, chunks.Count);
        Assert.All(chunks, chunk => Assert.Equal("guide.md", chunk.Source));
        Assert.Equal(first, chunks[0].Content);
        Assert.Equal(JoinParagraphs(first, second), chunks[1].Content);
        Assert.Equal(JoinParagraphs(second, third), chunks[2].Content);
    }

    [Fact]
    public async Task GetDocumentChunksAsync_WhenSplitting_CarriesLastParagraphIntoNextChunk()
    {
        var first = Paragraph(400, 'a');
        var second = Paragraph(150, 'b');
        var third = Paragraph(150, 'c');
        WriteMarkdown("guide.md", JoinParagraphs(first, second, third));

        var chunks = await CreateSut().GetDocumentChunksAsync();

        Assert.Contains(first, chunks[1].Content);
        Assert.Contains(second, chunks[1].Content);
        Assert.Contains(second, chunks[2].Content);
        Assert.Contains(third, chunks[2].Content);
        Assert.StartsWith(second, chunks[2].Content);
    }

    [Fact]
    public async Task GetDocumentChunksAsync_WhenSingleParagraphExceedsChunkSize_KeepsItAsOneChunk()
    {
        var oversized = Paragraph(ChunkSize + 80, 'x');
        WriteMarkdown("long.md", oversized);

        var chunks = await CreateSut().GetDocumentChunksAsync();

        var chunk = Assert.Single(chunks);
        Assert.Equal(oversized, chunk.Content);
        Assert.Equal("long.md", chunk.Source);
    }

    [Fact]
    public async Task GetDocumentChunksAsync_WhenFileUsesWindowsLineEndings_StillSplitsOnParagraphs()
    {
        var first = Paragraph(400, 'a');
        var second = Paragraph(150, 'b');
        File.WriteAllText(
            Path.Combine(_knowledgeBasePath, "windows.md"),
            first + "\r\n\r\n" + second);

        var chunks = await CreateSut().GetDocumentChunksAsync();

        Assert.Equal(2, chunks.Count);
        Assert.Equal(first, chunks[0].Content);
        Assert.Equal(JoinParagraphs(first, second), chunks[1].Content);
    }

    [Fact]
    public async Task GetDocumentChunksAsync_WhenFileIsEmpty_ReturnsNoChunks()
    {
        WriteMarkdown("empty.md", string.Empty);

        var chunks = await CreateSut().GetDocumentChunksAsync();

        Assert.Empty(chunks);
    }

    [Fact]
    public async Task GetDocumentChunksAsync_WhenFileIsOnlyWhitespace_ReturnsNoChunks()
    {
        WriteMarkdown("blank.md", "  \n\n\t\r\n");

        var chunks = await CreateSut().GetDocumentChunksAsync();

        Assert.Empty(chunks);
    }

    [Fact]
    public async Task GetDocumentChunksAsync_WhenMultipleFiles_PreservesSourcePerFile()
    {
        WriteMarkdown("architecture.md", "Architecture overview.");
        WriteMarkdown("security.md", "Security overview.");

        var chunks = await CreateSut().GetDocumentChunksAsync();

        Assert.Equal(2, chunks.Count);
        Assert.Contains(chunks, chunk => chunk.Source == "architecture.md" && chunk.Content == "Architecture overview.");
        Assert.Contains(chunks, chunk => chunk.Source == "security.md" && chunk.Content == "Security overview.");
    }

    [Fact]
    public async Task GetDocumentChunksAsync_WhenNonMarkdownFilesExist_IgnoresThem()
    {
        WriteMarkdown("keep.md", "Keep this.");
        File.WriteAllText(Path.Combine(_knowledgeBasePath, "ignore.txt"), "Ignore this.");

        var chunks = await CreateSut().GetDocumentChunksAsync();

        var chunk = Assert.Single(chunks);
        Assert.Equal("keep.md", chunk.Source);
        Assert.Equal("Keep this.", chunk.Content);
    }

    [Fact]
    public async Task GetDocumentChunksAsync_WhenKnowledgebaseIsMissing_ThrowsDirectoryNotFoundException()
    {
        Directory.Delete(_knowledgeBasePath);

        await Assert.ThrowsAsync<DirectoryNotFoundException>(
            () => CreateSut().GetDocumentChunksAsync());
    }

    private DocumentService CreateSut()
        => new(new FakeWebHostEnvironment { ContentRootPath = _contentRoot });

    private void WriteMarkdown(string fileName, string content)
        => File.WriteAllText(Path.Combine(_knowledgeBasePath, fileName), content);

    private static string Paragraph(int length, char fill)
        => new(fill, length);

    private static string JoinParagraphs(params string[] paragraphs)
        => string.Join("\n\n", paragraphs);

    private sealed class FakeWebHostEnvironment : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";
        public string ApplicationName { get; set; } = "AskMyDocs.Tests";
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = string.Empty;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }
}
