using System.Security.Cryptography;
using System.Text;
using AskMyDocs.API.Models;
using AskMyDocs.API.Services.AI;
using AskMyDocs.API.Services.Documents;
using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace AskMyDocs.API.Services.VectorStore;

public class VectorStoreService(QdrantClient qdrantClient, IEmbeddingService embeddingService) : IVectorStoreService
{
	private const string CollectionName = "knowledge_base";

	public async Task StoreAsync(IReadOnlyList<DocumentChunk> chunks, CancellationToken cancellationToken = default)
	{
		var points = new List<PointStruct>();

		foreach (var chunk in chunks)
		{
			var embedding =
				await embeddingService.GenerateEmbeddingAsync(chunk.Content, cancellationToken);

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
			points,
			cancellationToken: cancellationToken);
	}

	public async Task<List<DocumentSearchResult>> SearchAsync(float[] embedding, int limit = 5, CancellationToken cancellationToken = default)
	{
		var results = await qdrantClient.QueryAsync(
			CollectionName,
			embedding,
			limit: (ulong)limit,
			cancellationToken: cancellationToken);

		return results
			.Where(x => x.Payload.ContainsKey("content"))
			.Select(x => new DocumentSearchResult(
				x.Payload["content"].StringValue,
				x.Payload["source"].StringValue,
				x.Score))
			.ToList();
	}

	public async Task EnsureCollectionAsync(CancellationToken cancellationToken = default)
	{
		var collections = await qdrantClient.ListCollectionsAsync(cancellationToken: cancellationToken);

		if (collections.Contains(CollectionName))
			return;

		await qdrantClient.CreateCollectionAsync(
			CollectionName,
			new VectorParams
			{
				Size = 768,
				Distance = Distance.Cosine
			},
			cancellationToken: cancellationToken);
	}

	private static Guid CreateDeterministicId(DocumentChunk chunk)
	{
		var input = $"{chunk.Source}:{chunk.Content}";
		var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));

		return new Guid(hash[..16]);
	}
}
