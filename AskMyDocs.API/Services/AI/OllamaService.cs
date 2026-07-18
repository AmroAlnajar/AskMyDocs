using AskMyDocs.API.Models;
using Microsoft.Extensions.Options;

namespace AskMyDocs.API.Services.AI;

public class OllamaService(
	HttpClient httpClient,
	IOptions<OllamaOptions> options,
	ILogger<OllamaService> logger) : IOllamaService
{
	public async Task<string> ChatAsync(string message)
	{
		var request = new
		{
			model = options.Value.Model,
			prompt = message,
			stream = false
		};

		try
		{
			var response = await httpClient.PostAsJsonAsync("api/generate", request);

			if (!response.IsSuccessStatusCode)
			{
				logger.LogWarning("Ollama returned {StatusCode}", (int)response.StatusCode);
				throw new OllamaUnavailableException($"Ollama returned {(int)response.StatusCode}.");
			}

			var result = await response.Content.ReadFromJsonAsync<OllamaResponse>();
			return result?.Response ?? string.Empty;
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
}
