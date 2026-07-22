using AskMyDocs.API.Models;
using AskMyDocs.API.Services.Documents;

namespace AskMyDocs.API.Services.VectorStore;

public interface IVectorStoreService
{
	Task EnsureCollectionAsync(CancellationToken cancellationToken = default);

	Task StoreAsync(IReadOnlyList<DocumentChunk> chunks, CancellationToken cancellationToken = default);

	Task<List<DocumentSearchResult>> SearchAsync(float[] embedding, int limit = 5, CancellationToken cancellationToken = default);
}
