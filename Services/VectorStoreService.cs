using System.Security.Cryptography;
using System.Text;
using askmydocs.Models;
using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace askmydocs.Services;

public class VectorStoreService(QdrantClient qdrantClient, IEmbeddingService embeddingService) : IVectorStoreService
{
	private const string CollectionName = "knowledge_base";

	public async Task StoreAsync(IReadOnlyList<DocumentChunk> chunks)
	{
		var points = new List<PointStruct>();

		foreach (var chunk in chunks)
		{
			var embedding =
				await embeddingService.GenerateEmbeddingAsync(chunk.Content);

			var point = new PointStruct
			{
				Id = CreateDeterministicId(chunk),
				Vectors = embedding,
				Payload =
				{
					["content"] = chunk.Content,
					["source"] = chunk.Source
				}
			};

			points.Add(point);
		}

		await qdrantClient.UpsertAsync(
			CollectionName,
			points);
	}

	public async Task<List<DocumentSearchResult>> SearchAsync(float[] embedding, int limit = 5)
	{
		var results = await qdrantClient.QueryAsync(CollectionName, embedding, limit: (ulong)limit);

		return results
			.Where(x => x.Payload.ContainsKey("content"))
			.Select(x => new DocumentSearchResult(
				x.Payload["content"].StringValue,
				x.Payload["source"].StringValue,
				x.Score))
			.ToList();
	}

	private static Guid CreateDeterministicId(DocumentChunk chunk)
	{
		var input = $"{chunk.Source}:{chunk.Content}";
		var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));

		return new Guid(hash[..16]);
	}
}
