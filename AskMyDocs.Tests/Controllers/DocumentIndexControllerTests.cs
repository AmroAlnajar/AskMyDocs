using System.Text.Json;
using AskMyDocs.API.Controllers;
using AskMyDocs.API.Models;
using AskMyDocs.API.Services.Documents;
using AskMyDocs.API.Services.VectorStore;
using Microsoft.AspNetCore.Mvc;

namespace AskMyDocs.Tests.Controllers;

public class DocumentIndexControllerTests
{
	private readonly FakeDocumentService _documents = new();
	private readonly FakeVectorStoreService _vectorStore = new();
	private readonly DocumentIndexController _sut;

	public DocumentIndexControllerTests()
	{
		_sut = new DocumentIndexController(_documents, _vectorStore);
	}

	[Fact]
	public async Task Index_StoresChunksReturnedByDocumentService()
	{
		_documents.Chunks =
		[
			new DocumentChunk("Auth uses JWT.", "authentication.md"),
			new DocumentChunk("Use TLS in transit.", "security.md")
		];

		await _sut.Index();

		Assert.Same(_documents.Chunks, _vectorStore.StoredChunks);
	}

	[Fact]
	public async Task Index_WhenChunksExist_ReturnsSuccessPayloadWithCounts()
	{
		_documents.Chunks =
		[
			new DocumentChunk("JWT overview.", "authentication.md"),
			new DocumentChunk("JWT details.", "authentication.md"),
			new DocumentChunk("TLS in transit.", "security.md")
		];

		var result = await _sut.Index();

		var body = GetOkBody(result);
		Assert.Equal("Documents indexed successfully.", body.GetProperty("message").GetString());
		Assert.Equal(2, body.GetProperty("documents").GetInt32());
		Assert.Equal(3, body.GetProperty("chunks").GetInt32());
	}

	[Fact]
	public async Task Index_WhenNoChunks_ReturnsZeroCountsAndStillStores()
	{
		_documents.Chunks = [];

		var result = await _sut.Index();

		Assert.Same(_documents.Chunks, _vectorStore.StoredChunks);
		var body = GetOkBody(result);
		Assert.Equal(0, body.GetProperty("documents").GetInt32());
		Assert.Equal(0, body.GetProperty("chunks").GetInt32());
	}

	[Fact]
	public async Task Index_WhenDocumentServiceFails_DoesNotStore()
	{
		_documents.Exception = new DirectoryNotFoundException("Knowledgebase is missing");

		await Assert.ThrowsAsync<DirectoryNotFoundException>(_sut.Index);

		Assert.False(_vectorStore.StoreCalled);
	}

	[Fact]
	public async Task Index_WhenStoreFails_BubblesException()
	{
		_documents.Chunks =
		[
			new DocumentChunk("Auth uses JWT.", "authentication.md")
		];
		_vectorStore.StoreException = new InvalidOperationException("Qdrant is unavailable");

		var ex = await Assert.ThrowsAsync<InvalidOperationException>(_sut.Index);

		Assert.Equal("Qdrant is unavailable", ex.Message);
		Assert.True(_vectorStore.StoreCalled);
	}

	private static JsonElement GetOkBody(IActionResult result)
	{
		var ok = Assert.IsType<OkObjectResult>(result);
		Assert.NotNull(ok.Value);
		return JsonSerializer.SerializeToElement(ok.Value);
	}

	private sealed class FakeDocumentService : IDocumentService
	{
		public List<DocumentChunk> Chunks { get; set; } = [];
		public Exception? Exception { get; set; }

		public Task<List<DocumentChunk>> GetDocumentChunksAsync()
		{
			if (Exception is not null)
			{
				throw Exception;
			}

			return Task.FromResult(Chunks);
		}
	}

	private sealed class FakeVectorStoreService : IVectorStoreService
	{
		public IReadOnlyList<DocumentChunk>? StoredChunks { get; private set; }
		public bool StoreCalled { get; private set; }
		public Exception? StoreException { get; set; }

		public Task EnsureCollectionAsync() => Task.CompletedTask;

		public Task StoreAsync(IReadOnlyList<DocumentChunk> chunks)
		{
			StoreCalled = true;

			if (StoreException is not null)
			{
				throw StoreException;
			}

			StoredChunks = chunks;
			return Task.CompletedTask;
		}

		public Task<List<DocumentSearchResult>> SearchAsync(float[] embedding, int limit = 5)
			=> Task.FromResult(new List<DocumentSearchResult>());
	}
}
