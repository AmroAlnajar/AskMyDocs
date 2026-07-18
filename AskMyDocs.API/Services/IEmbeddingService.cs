namespace AskMyDocs.API.Services
{
	public interface IEmbeddingService
	{
		Task<float[]> GenerateEmbeddingAsync(string text);
	}
}
