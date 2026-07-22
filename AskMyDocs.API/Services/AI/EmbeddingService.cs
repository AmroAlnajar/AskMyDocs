using AskMyDocs.API.Models;
using Microsoft.Extensions.Options;

namespace AskMyDocs.API.Services.AI;

public class EmbeddingService(
	HttpClient httpClient,
	IOptions<OllamaOptions> options,
	ILogger<EmbeddingService> logger) : IEmbeddingService
{
	public async Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
	{
		var request = new
		{
			model = options.Value.EmbeddingModel,
			prompt = text
		};

		try
		{
			var response = await httpClient.PostAsJsonAsync(
				"api/embeddings",
				request,
				cancellationToken);

			if (!response.IsSuccessStatusCode)
			{
				logger.LogWarning("Ollama embeddings returned {StatusCode}", (int)response.StatusCode);
				throw new OllamaUnavailableException($"Ollama returned {(int)response.StatusCode}.");
			}

			var result = await response.Content
				.ReadFromJsonAsync<EmbeddingResponse>(cancellationToken);

			return result?.Embedding
				?? throw new InvalidOperationException("No embedding returned.");
		}
		catch (OllamaUnavailableException)
		{
			throw;
		}
		catch (HttpRequestException ex)
		{
			logger.LogWarning(ex, "Ollama is unreachable");
			throw new OllamaUnavailableException("Ollama is unavailable.", ex);
		}
		catch (TaskCanceledException ex)
		{
			logger.LogWarning(ex, "Ollama request timed out");
			throw new OllamaUnavailableException("Ollama timed out.", ex);
		}
	}

	private sealed class EmbeddingResponse
	{
		public float[] Embedding { get; set; } = [];
	}
}
