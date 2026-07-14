using askmydocs.Models;

namespace askmydocs.Services;

public class RagService(
	IEmbeddingService embeddingService,
	IVectorStoreService vectorStoreService,
	IOllamaService ollamaService) : IRagService
{
	public async Task<RagResponse> AskAsync(string question)
	{
		// 1. Convert the question into a vector
		var embedding =
			await embeddingService.GenerateEmbeddingAsync(question);

		// 2. Find relevant chunks
		var chunks =
			await vectorStoreService.SearchAsync(embedding, 5);

		// 3. Build context for the LLM
		var context = string.Join(
			"\n\n---\n\n",
			chunks.Select(x =>
				$"Source: {x.Source}\n{x.Content}"));

		// 4. Ask the LLM using the retrieved context
		var prompt = $"""
            You are a helpful assistant answering questions
            about the Helix system.

            Answer the user's question using ONLY the provided
            context.

            If the answer cannot be found in the context,
            say that you don't have enough information.

            Context:
            {context}

            User question:
            {question}
            """;

		var answer = await ollamaService.ChatAsync(prompt);

		var sources = chunks
			.Select(x => x.Source)
			.Distinct()
			.ToList();

		return new RagResponse(answer, sources);
	}
}