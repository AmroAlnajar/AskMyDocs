using AskMyDocs.API.Models;
using AskMyDocs.API.Services.AI;
using AskMyDocs.API.Services.Documents;
using AskMyDocs.API.Services.RAG;
using AskMyDocs.API.Services.VectorStore;

namespace AskMyDocs.Tests.Services;

public class RagServiceTests
{
	private readonly List<string> _calls = [];
	private readonly FakeEmbeddingService _embeddings;
	private readonly FakeVectorStoreService _vectorStore;
	private readonly FakeOllamaService _ollama;
	private readonly RagService _sut;

	public RagServiceTests()
	{
		_embeddings = new FakeEmbeddingService(_calls);
		_vectorStore = new FakeVectorStoreService(_calls);
		_ollama = new FakeOllamaService(_calls);
		_sut = new RagService(_embeddings, _vectorStore, _ollama);
	}

	[Fact]
	public async Task AskAsync_EmbedsQuestionThenSearchesTopFiveThenChats()
	{
		_vectorStore.Results =
		[
			new DocumentSearchResult("Auth uses JWT.", "authentication.md", 0.9f)
		];

		await _sut.AskAsync("How does auth work?");

		Assert.Equal(["embed", "search", "chat"], _calls);
		Assert.Equal("How does auth work?", _embeddings.LastText);
		Assert.Equal(_embeddings.Embedding, _vectorStore.LastEmbedding);
		Assert.Equal(5, _vectorStore.LastLimit);
	}

	[Fact]
	public async Task AskAsync_WhenChunksExist_ReturnsLlmAnswer()
	{
		_vectorStore.Results =
		[
			new DocumentSearchResult("Auth uses JWT.", "authentication.md", 0.9f)
		];
		_ollama.Answer = "Authentication uses JWT tokens.";

		var response = await _sut.AskAsync("How does auth work?");

		Assert.Equal("Authentication uses JWT tokens.", response.Answer);
	}

	[Fact]
	public async Task AskAsync_WhenChunksExist_GroupsSourcesByFileAndKeepsHighestScore()
	{
		_vectorStore.Results =
		[
			new DocumentSearchResult("JWT overview.", "authentication.md", 0.4f),
			new DocumentSearchResult("JWT details.", "authentication.md", 0.9f),
			new DocumentSearchResult("TLS in transit.", "security.md", 0.7f)
		];

		var response = await _sut.AskAsync("How is auth secured?");

		Assert.Equal(2, response.Sources.Count);
		Assert.Contains(response.Sources, source => source.Document == "authentication.md" && source.Score == 0.9f);
		Assert.Contains(response.Sources, source => source.Document == "security.md" && source.Score == 0.7f);
	}

	[Fact]
	public async Task AskAsync_WhenChunksExist_PromptIncludesContextSourcesAndQuestion()
	{
		_vectorStore.Results =
		[
			new DocumentSearchResult("Use JWT.", "authentication.md", 0.9f),
			new DocumentSearchResult("Use TLS.", "security.md", 0.8f)
		];

		await _sut.AskAsync("How does auth work?");

		Assert.Contains("How does auth work?", _ollama.LastPrompt);
		Assert.Contains("Source: authentication.md", _ollama.LastPrompt);
		Assert.Contains("Use JWT.", _ollama.LastPrompt);
		Assert.Contains("Source: security.md", _ollama.LastPrompt);
		Assert.Contains("Use TLS.", _ollama.LastPrompt);
		Assert.Contains("---", _ollama.LastPrompt);
		Assert.Contains("ONLY the provided", _ollama.LastPrompt);
	}

	[Fact]
	public async Task AskAsync_WhenNoChunks_StillAsksLlmAndReturnsEmptySources()
	{
		_vectorStore.Results = [];
		_ollama.Answer = "I don't have enough information.";

		var response = await _sut.AskAsync("What is Helix?");

		Assert.Equal(["embed", "search", "chat"], _calls);
		Assert.Equal("I don't have enough information.", response.Answer);
		Assert.Empty(response.Sources);
		Assert.Contains("What is Helix?", _ollama.LastPrompt);
		Assert.Contains("Context:", _ollama.LastPrompt);
	}

	[Fact]
	public async Task AskAsync_WhenEmbeddingFails_DoesNotSearchOrChat()
	{
		_embeddings.Exception = new InvalidOperationException("embedding failed");

		var ex = await Assert.ThrowsAsync<InvalidOperationException>(
			() => _sut.AskAsync("How does auth work?"));

		Assert.Equal("embedding failed", ex.Message);
		Assert.Equal(["embed"], _calls);
	}

	[Fact]
	public async Task AskAsync_WhenOllamaFails_BubblesException()
	{
		_vectorStore.Results =
		[
			new DocumentSearchResult("Use JWT.", "authentication.md", 0.9f)
		];
		_ollama.Exception = new OllamaUnavailableException("Ollama is down");

		var ex = await Assert.ThrowsAsync<OllamaUnavailableException>(
			() => _sut.AskAsync("How does auth work?"));

		Assert.Equal("Ollama is down", ex.Message);
		Assert.Equal(["embed", "search", "chat"], _calls);
	}

	private sealed class FakeEmbeddingService(List<string> calls) : IEmbeddingService
	{
		public float[] Embedding { get; } = [0.11f, 0.22f, 0.33f];
		public string? LastText { get; private set; }
		public Exception? Exception { get; set; }

		public Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
		{
			calls.Add("embed");
			LastText = text;

			if (Exception is not null)
			{
				throw Exception;
			}

			return Task.FromResult(Embedding);
		}
	}

	private sealed class FakeVectorStoreService(List<string> calls) : IVectorStoreService
	{
		public List<DocumentSearchResult> Results { get; set; } = [];
		public float[]? LastEmbedding { get; private set; }
		public int? LastLimit { get; private set; }

		public Task EnsureCollectionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

		public Task StoreAsync(IReadOnlyList<DocumentChunk> chunks, CancellationToken cancellationToken = default) => Task.CompletedTask;

		public Task<List<DocumentSearchResult>> SearchAsync(float[] embedding, int limit = 5, CancellationToken cancellationToken = default)
		{
			calls.Add("search");
			LastEmbedding = embedding;
			LastLimit = limit;
			return Task.FromResult(Results);
		}
	}

	private sealed class FakeOllamaService(List<string> calls) : IOllamaService
	{
		public string Answer { get; set; } = "OK";
		public string? LastPrompt { get; private set; }
		public Exception? Exception { get; set; }

		public Task<string> ChatAsync(string message, CancellationToken cancellationToken = default)
		{
			calls.Add("chat");
			LastPrompt = message;

			if (Exception is not null)
			{
				throw Exception;
			}

			return Task.FromResult(Answer);
		}
	}
}
