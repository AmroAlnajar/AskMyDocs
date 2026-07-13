namespace askmydocs.Services;

public interface IVectorStoreService
{
	Task StoreAsync(IReadOnlyList<DocumentChunk> chunks);
	Task<List<DocumentChunk>> SearchAsync(
	float[] embedding,
	int limit = 5);
}