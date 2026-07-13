namespace askmydocs.Models;

public class OllamaOptions
{
	public const string SectionName = "Ollama";

	public string BaseUrl { get; set; } = string.Empty;

	public string Model { get; set; } = string.Empty;

	public string EmbeddingModel { get; set; } = string.Empty;
}
