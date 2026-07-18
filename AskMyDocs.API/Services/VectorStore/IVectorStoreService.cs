using AskMyDocs.API.Models;
using AskMyDocs.API.Services.Documents;

namespace AskMyDocs.API.Services.VectorStore;

public interface IVectorStoreService
{
	Task EnsureCollectionAsync();

	Task StoreAsync(IReadOnlyList<DocumentChunk> chunks);

	Task<List<DocumentSearchResult>> SearchAsync(float[] embedding, int limit = 5);
}