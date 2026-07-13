using askmydocs.Models;
using askmydocs.Services;
using Microsoft.AspNetCore.Mvc;

namespace askmydocs.Controllers;

[ApiController]
[Route("api/[controller]")]
public class EmbeddingController(IEmbeddingService embeddingService) : ControllerBase
{
	[HttpPost]
	public async Task<IActionResult> Generate([FromBody] EmbeddingRequest request)
	{
		var embedding = await embeddingService.GenerateEmbeddingAsync(request.Text);

		return Ok(new
		{
			dimensions = embedding.Length,
			embedding
		});
	}
}
