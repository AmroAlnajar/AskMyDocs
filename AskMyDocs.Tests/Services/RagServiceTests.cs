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
	public async Task AskAsync_EmbedsQuestionThenSearchesCandidatesThenChats()
	{
		_vectorStore.Results =
		[
			new DocumentSearchResult("Auth uses JWT.", "authentication.md", 0.9f)
		];

		await _sut.AskAsync("How does auth work?");

		Assert.Equal(["embed", "search", "chat"], _calls);
		Assert.Equal("How does auth work?", _embeddings.LastText);
		Assert.Equal(_embeddings.Embedding, _vectorStore.LastEmbedding);
		Assert.Equal(RagService.CandidateLimit, _vectorStore.LastLimit);
	}

	[Fact]
	public async Task AskAsync_WhenChunksExist_ReturnsCleanedLlmAnswer()
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
	public async Task AskAsync_WhenChunksExist_GroupsSourcesByFileAndKeepsHighestScoreAndHeading()
	{
		_vectorStore.Results =
		[
			new DocumentSearchResult("JWT overview.", "authentication.md", 0.4f, "Overview"),
			new DocumentSearchResult("JWT details.", "authentication.md", 0.9f, "Tokens"),
			new DocumentSearchResult("TLS in transit.", "security.md", 0.7f, "TLS")
		];

		var response = await _sut.AskAsync("How is auth secured?");

		Assert.Equal(2, response.Sources.Count);
		Assert.Equal("authentication.md", response.Sources[0].Document);
		Assert.Equal(0.9f, response.Sources[0].Score);
		Assert.Equal("Tokens", response.Sources[0].Heading);
		Assert.Equal("security.md", response.Sources[1].Document);
		Assert.Equal(0.7f, response.Sources[1].Score);
		Assert.Equal("TLS", response.Sources[1].Heading);
	}

	[Fact]
	public async Task AskAsync_WhenChunksExist_PromptIncludesRulesContextSourcesAndQuestion()
	{
		_vectorStore.Results =
		[
			new DocumentSearchResult("Use JWT.", "authentication.md", 0.9f, "Tokens"),
			new DocumentSearchResult("Use TLS.", "security.md", 0.8f)
		];

		await _sut.AskAsync("How does auth work?");

		Assert.Contains("How does auth work?", _ollama.LastPrompt);
		Assert.Contains("authentication.md | Tokens", _ollama.LastPrompt);
		Assert.Contains("Use JWT.", _ollama.LastPrompt);
		Assert.Contains("[2] security.md", _ollama.LastPrompt);
		Assert.Contains("Use TLS.", _ollama.LastPrompt);
		Assert.Contains("Do not guess", _ollama.LastPrompt);
		Assert.Contains("cite the source as [filename.md]", _ollama.LastPrompt);
		Assert.Contains(RagService.InsufficientContext, _ollama.LastPrompt);
	}

	[Fact]
	public async Task AskAsync_WhenNoChunks_DoesNotCallLlmAndReturnsInsufficientContext()
	{
		_vectorStore.Results = [];

		var response = await _sut.AskAsync("What is Helix?");

		Assert.Equal(["embed", "search"], _calls);
		Assert.Equal(RagService.InsufficientContext, response.Answer);
		Assert.Empty(response.Sources);
		Assert.Null(_ollama.LastPrompt);
	}

	[Fact]
	public async Task AskAsync_WhenAllChunksAreBelowScoreFloor_DoesNotCallLlm()
	{
		_vectorStore.Results =
		[
			new DocumentSearchResult("Unrelated text.", "architecture.md", RagService.MinRelevanceScore - 0.01f)
		];

		var response = await _sut.AskAsync("How does auth work?");

		Assert.Equal(["embed", "search"], _calls);
		Assert.Equal(RagService.InsufficientContext, response.Answer);
		Assert.Empty(response.Sources);
	}

	[Fact]
	public async Task AskAsync_WhenSomeChunksAreWeak_PromptOmitsThem()
	{
		_vectorStore.Results =
		[
			new DocumentSearchResult("Use JWT.", "authentication.md", 0.9f),
			new DocumentSearchResult("Unrelated.", "architecture.md", 0.1f)
		];

		await _sut.AskAsync("How does auth work?");

		Assert.Contains("Use JWT.", _ollama.LastPrompt);
		Assert.DoesNotContain("Unrelated.", _ollama.LastPrompt);
		Assert.DoesNotContain("architecture.md", _ollama.LastPrompt);
	}

	[Fact]
	public async Task AskAsync_StripsThinkBlocksFromModelOutput()
	{
		_vectorStore.Results =
		[
			new DocumentSearchResult("Use JWT.", "authentication.md", 0.9f)
		];
		_ollama.Answer = "<think>planning</think>\nHelix uses JWT [authentication.md].";

		var response = await _sut.AskAsync("How does auth work?");

		Assert.Equal("Helix uses JWT [authentication.md].", response.Answer);
	}

	[Fact]
	public async Task AskAsync_WhenModelReturnsOnlyThinkBlock_ReturnsInsufficientContext()
	{
		_vectorStore.Results =
		[
			new DocumentSearchResult("Use JWT.", "authentication.md", 0.9f)
		];
		_ollama.Answer = "<think>no usable answer</think>\n  ";

		var response = await _sut.AskAsync("How does auth work?");

		Assert.Equal(RagService.InsufficientContext, response.Answer);
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

	[Fact]
	public void SelectContext_SkipsOverlapDuplicatesAndCapsCount()
	{
		var shared = new string('a', 80);
		var results = new List<DocumentSearchResult>
		{
			new(shared + " extra.", "a.md", 0.9f),
			new(shared, "a.md", 0.85f),
			new("Other fact.", "b.md", 0.8f)
		};

		var selected = RagService.SelectContext(results);

		Assert.Equal(2, selected.Count);
		Assert.DoesNotContain(selected, chunk => chunk.Content == shared);
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
