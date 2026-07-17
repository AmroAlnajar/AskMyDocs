using askmydocs.Models;
using askmydocs.Services;
using Microsoft.AspNetCore.Mvc;

namespace askmydocs.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ChatController(IRagService ragService) : ControllerBase
{
	[HttpPost]
	public async Task<IActionResult> Chat([FromBody] ChatRequest request)
	{
		var response = await ragService.AskAsync(request.Message);

		return Ok(response);
	}
}