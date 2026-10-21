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
		await ResetCollectionAsync(cancellationToken);

		if (chunks.Count == 0)
			return;

		var points = new List<PointStruct>();

		foreach (var chunk in chunks)
		{
			var embedding =
				await embeddingService.GenerateEmbeddingAsync(chunk.EmbeddingText, cancellationToken);

			var point = new PointStruct
			{
				Id = CreateDeterministicId(chunk),
				Vectors = embedding,
				Payload =
				{
					["content"] = chunk.Content,
					["source"] = chunk.Source,
					["heading"] = chunk.Heading,
					["index"] = (long)chunk.Index
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
				x.Payload.TryGetValue("source", out var source) ? source.StringValue : "",
				x.Score,
				x.Payload.TryGetValue("heading", out var heading) ? heading.StringValue : ""))
			.ToList();
	}

	public async Task EnsureCollectionAsync(CancellationToken cancellationToken = default)
	{
		var collections = await qdrantClient.ListCollectionsAsync(cancellationToken: cancellationToken);

		if (collections.Contains(CollectionName))
			return;

		await CreateCollectionAsync(cancellationToken);
	}

	private async Task ResetCollectionAsync(CancellationToken cancellationToken)
	{
		var collections = await qdrantClient.ListCollectionsAsync(cancellationToken: cancellationToken);

		if (collections.Contains(CollectionName))
			await qdrantClient.DeleteCollectionAsync(CollectionName, cancellationToken: cancellationToken);

		await CreateCollectionAsync(cancellationToken);
	}

	private Task CreateCollectionAsync(CancellationToken cancellationToken)
		=> qdrantClient.CreateCollectionAsync(
			CollectionName,
			new VectorParams
			{
				Size = 768,
				Distance = Distance.Cosine
			},
			cancellationToken: cancellationToken);

	private static Guid CreateDeterministicId(DocumentChunk chunk)
	{
		var input = $"{chunk.Source}:{chunk.Heading}:{chunk.Index}:{chunk.Content}";
		var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));

		return new Guid(hash[..16]);
	}
}
