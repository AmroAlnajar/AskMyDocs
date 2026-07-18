using AskMyDocs.API.Models;

namespace AskMyDocs.API.Services;

public interface IVectorStoreService
{
	Task EnsureCollectionAsync();

	Task StoreAsync(IReadOnlyList<DocumentChunk> chunks);

	Task<List<DocumentSearchResult>> SearchAsync(float[] embedding, int limit = 5);
}