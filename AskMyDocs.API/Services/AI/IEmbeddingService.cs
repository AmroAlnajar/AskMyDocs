namespace AskMyDocs.API.Services.AI;

public interface IEmbeddingService
{
	Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default);
}
