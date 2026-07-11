using askmydocs.Models;
using askmydocs.Services;
using Microsoft.AspNetCore.Mvc;

namespace askmydocs.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ChatController(IOllamaService ollamaService) : ControllerBase
{
	[HttpPost]
	public async Task<IActionResult> Chat([FromBody] ChatRequest request)
	{
		try
		{
			var response = await ollamaService.ChatAsync(request.Message);
			return Ok(new { response });
		}
		catch (OllamaUnavailableException)
		{
			return StatusCode(StatusCodes.Status503ServiceUnavailable, new
			{
				error = "The chat service is unavailable."
			});
		}
	}
}
