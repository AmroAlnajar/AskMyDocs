// Imports regular-expression support for cleaning model responses.
using System.Text.RegularExpressions;
// Imports the application's response and search-result models.
using AskMyDocs.API.Models;
// Imports AI service contracts.
using AskMyDocs.API.Services.AI;
// Imports vector-store service contracts.
using AskMyDocs.API.Services.VectorStore;

// Declares the retrieval-augmented-generation service namespace.
namespace AskMyDocs.API.Services.RAG;

// Coordinates embedding, search, context selection, prompting, and answer cleanup.
public class RagService(IEmbeddingService embeddingService, IVectorStoreService vectorStoreService, IOllamaService ollamaService) : IRagService
{
	// Limits the number of vector-search candidates considered.
	internal const int CandidateLimit = 10;
	// Limits the number of chunks sent to the model.
	internal const int MaxContextChunks = 5;
	// Defines the minimum acceptable search relevance score.
	internal const float MinRelevanceScore = 0.4f;
	// Defines the exact fallback answer when context is insufficient.
	internal const string InsufficientContext = "I don't have enough information.";

	// Matches model <think> blocks so they can be removed from visible answers.
	private static readonly Regex ThinkBlock = new(
		// Matches across line breaks until the closing think tag.
		@"<think>[\s\S]*?</think>",
		// Enables case-insensitive matching and compiles the reusable expression.
		RegexOptions.IgnoreCase | RegexOptions.Compiled);

	// Answers a question from retrieved document evidence.
	public async Task<RagResponse> AskAsync(string question, CancellationToken cancellationToken = default)
	{
		// Holds the vector representation of the question.
		var embedding =
			// Generates the question embedding while honoring cancellation.
			await embeddingService.GenerateEmbeddingAsync(question, cancellationToken);

		// Holds the top matching document chunks.
		var candidates =
			// Searches using the embedding and configured candidate limit.
			await vectorStoreService.SearchAsync(embedding, CandidateLimit, cancellationToken);

		// Filters matches down to distinct, relevant context.
		var chunks = SelectContext(candidates);

		// Handles the absence of usable evidence.
		if (chunks.Count == 0)
		{
			// Returns the fallback answer with no sources.
			return new RagResponse(InsufficientContext, []);
		}

		// Constructs the model prompt from the question and context.
		var prompt = BuildPrompt(question, chunks);
		// Generates and sanitizes the answer.
		var answer = CleanAnswer(
			// Sends the constrained prompt to the chat model.
			await ollamaService.ChatAsync(prompt, cancellationToken));

		// Builds one best reference per source document.
		var sources = chunks
			// Groups context chunks by their source file.
			.GroupBy(x => x.Source)
			// Converts each group to its best source reference.
			.Select(group =>
			{
				// Finds the group's highest-scoring chunk.
				var best = group.MaxBy(x => x.Score)!;
				// Creates the source citation using that chunk's metadata.
				return new SourceReference(group.Key, best.Score, best.Heading);
			})
			// Sorts references from most to least relevant.
			.OrderByDescending(x => x.Score)
			// Materializes the query as a list.
			.ToList();

		// Returns the answer and its supporting sources.
		return new RagResponse(answer, sources);
	}

	// Chooses relevant, non-overlapping chunks for context.
	internal static List<DocumentSearchResult> SelectContext(IReadOnlyList<DocumentSearchResult> candidates)
	{
		// Starts the selected-context collection empty.
		var selected = new List<DocumentSearchResult>();

		// Considers only candidates at or above the relevance threshold.
		foreach (var chunk in candidates.Where(x => x.Score >= MinRelevanceScore))
		{
			// Stops when the context capacity is reached.
			if (selected.Count >= MaxContextChunks)
				// Exits the candidate loop.
				break;

			// Ignores chunks that duplicate selected content.
			if (IsDuplicate(selected, chunk))
				// Moves on without selecting the duplicate.
				continue;

			// Adds this distinct relevant chunk.
			selected.Add(chunk);
		}

		// Returns the ordered selection.
		return selected;
	}

	// Creates an evidence-only prompt for the chat model.
	internal static string BuildPrompt(string question, IReadOnlyList<DocumentSearchResult> chunks)
	{
		// Joins formatted chunks with blank-line separators.
		var context = string.Join(
			// Supplies the separator between chunks.
			"\n\n",
			// Numbers and formats each chunk.
			chunks.Select((chunk, i) => FormatChunk(i + 1, chunk)));

		// Returns the interpolated raw prompt string.
		return $"""
			Answer the question using only the context.

			Rules:
			- If the context does not contain the answer, reply exactly: {InsufficientContext}
			- Do not guess or use outside knowledge.
			- No preamble. No "based on the context".
			- Short sentences or bullets.
			- After each fact, cite the source as [filename.md].

			Context:
			{context}

			Question:
			{question}
			""";
	}

	// Removes empty output and hidden model reasoning from an answer.
	internal static string CleanAnswer(string answer)
	{
		// Detects a blank model response.
		if (string.IsNullOrWhiteSpace(answer))
			// Uses the standard fallback when no answer was returned.
			return InsufficientContext;

		// Strips think blocks and surrounding whitespace.
		var cleaned = ThinkBlock.Replace(answer, string.Empty).Trim();
		// Returns fallback if nothing visible remains; otherwise returns the cleaned answer.
		return string.IsNullOrWhiteSpace(cleaned)
			// Chooses the fallback for reasoning-only output.
			? InsufficientContext
			// Chooses the usable visible text.
			: cleaned;
	}

	// Formats one context chunk with its index and metadata.
	private static string FormatChunk(int index, DocumentSearchResult chunk)
	{
		// Computes an optional heading suffix.
		var heading = string.IsNullOrEmpty(chunk.Heading)
			// Supplies no suffix for a missing heading.
			? ""
			// Prepends a separator to a present heading.
			: $" | {chunk.Heading}";

		// Combines the citation label and chunk content.
		return $"[{index}] {chunk.Source}{heading}\n{chunk.Content}";
	}

	// Checks whether either text substantially contains the other.
	private static bool IsDuplicate(List<DocumentSearchResult> selected, DocumentSearchResult candidate)
	{
		// Returns true when any selected chunk overlaps by containment.
		return selected.Any(existing =>
			// Tests whether selected content contains candidate content.
			IsContained(existing.Content, candidate.Content) ||
			// Tests whether candidate content contains selected content.
			IsContained(candidate.Content, existing.Content));
	}

	// Determines whether the inner text is a sufficiently large substring of outer text.
	private static bool IsContained(string outer, string inner)
	{
		// Requires at least 80 shared characters and ordinal substring matching.
		return inner.Length >= 80 && outer.Contains(inner, StringComparison.Ordinal);
	}
}
