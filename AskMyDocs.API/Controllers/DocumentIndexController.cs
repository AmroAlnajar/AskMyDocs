using AskMyDocs.API.Services;
using Microsoft.AspNetCore.Mvc;

namespace AskMyDocs.API.Controllers;

[ApiController]
[Route("api/documents")]
public class DocumentIndexController(
	IDocumentService documentService,
	IVectorStoreService vectorStoreService) : ControllerBase
{
	[HttpPost("index")]
	public async Task<IActionResult> Index()
	{
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
