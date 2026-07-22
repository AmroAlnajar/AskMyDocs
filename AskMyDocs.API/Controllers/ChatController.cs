using AskMyDocs.API.Models;
using AskMyDocs.API.Services.RAG;
using Microsoft.AspNetCore.Mvc;

namespace AskMyDocs.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ChatController(IRagService ragService) : ControllerBase
{
	[HttpPost]
	public async Task<IActionResult> Chat([FromBody] ChatRequest request)
	{
		if (string.IsNullOrWhiteSpace(request.Message))
			return BadRequest(new { error = "Message is required." });

		var response = await ragService.AskAsync(request.Message);

		return Ok(response);
	}
}