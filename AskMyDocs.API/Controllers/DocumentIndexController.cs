using AskMyDocs.API.Services.Documents;
using AskMyDocs.API.Services.VectorStore;
using Microsoft.AspNetCore.Mvc;

namespace AskMyDocs.API.Controllers;

[ApiController]
[Route("api/documents")]
public class DocumentIndexController(
	IDocumentService documentService,
	IVectorStoreService vectorStoreService,
	IConfiguration configuration) : ControllerBase
{
	[HttpPost("index")]
	public async Task<IActionResult> Index([FromHeader(Name = "X-Api-Key")] string? apiKey)
	{
		var expected = configuration["IndexApiKey"];
		if (string.IsNullOrEmpty(expected) || apiKey != expected)
			return Unauthorized();

		var chunks = await documentService.GetDocumentChunksAsync();

		await vectorStoreService.StoreAsync(chunks);

		return Ok(new
		{
			message = "Documents indexed successfully.",
			documents = chunks
				.Select(x => x.Source)
				.Distinct()
				.Count(),
			chunks = chunks.Count
		});
	}
}
