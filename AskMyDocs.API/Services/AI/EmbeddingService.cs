using AskMyDocs.API.Models;
using Microsoft.Extensions.Options;

namespace AskMyDocs.API.Services.AI;

public class EmbeddingService(
	HttpClient httpClient,
	IOptions<OllamaOptions> options) : IEmbeddingService
{
	public async Task<float[]> GenerateEmbeddingAsync(string text)
	{
		var request = new
		{
			model = options.Value.EmbeddingModel,
			prompt = text
		};

		var response = await httpClient.PostAsJsonAsync(
			"api/embeddings",
			request);

		response.EnsureSuccessStatusCode();

		var result = await response.Content
			.ReadFromJsonAsync<EmbeddingResponse>();

		return result?.Embedding
			?? throw new InvalidOperationException("No embedding returned.");
	}

	private sealed class EmbeddingResponse
	{
		public float[] Embedding { get; set; } = [];
	}
}
