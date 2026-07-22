using AskMyDocs.API.Controllers;
using AskMyDocs.API.Models;
using AskMyDocs.API.Services.AI;
using AskMyDocs.API.Services.RAG;
using Microsoft.AspNetCore.Mvc;

namespace AskMyDocs.Tests.Controllers;

public class ChatControllerTests
{
	private readonly FakeRagService _ragService = new();
	private readonly ChatController _sut;

	public ChatControllerTests()
	{
		_sut = new ChatController(_ragService);
	}

	[Fact]
	public async Task Chat_WhenRequestIsValid_ReturnsOkWithRagResponse()
	{
		var expected = new RagResponse(
			"Authentication uses JWT tokens.",
			[new SourceReference("authentication.md", 0.9f)]);
		_ragService.Response = expected;

		var result = await _sut.Chat(new ChatRequest("How does auth work?"));

		var ok = Assert.IsType<OkObjectResult>(result);
		var body = Assert.IsType<RagResponse>(ok.Value);
		Assert.Equal(expected.Answer, body.Answer);
		var source = Assert.Single(body.Sources);
		Assert.Equal("authentication.md", source.Document);
		Assert.Equal(0.9f, source.Score);
	}

	[Fact]
	public async Task Chat_PassesMessageToRagService()
	{
		await _sut.Chat(new ChatRequest("How does auth work?"));

		Assert.Equal("How does auth work?", _ragService.LastQuestion);
	}

	[Fact]
	public async Task Chat_WhenMessageIsEmpty_ReturnsBadRequest()
	{
		var result = await _sut.Chat(new ChatRequest(""));

		Assert.IsType<BadRequestObjectResult>(result);
		Assert.Null(_ragService.LastQuestion);
	}

	[Fact]
	public async Task Chat_WhenMessageIsWhitespace_ReturnsBadRequest()
	{
		var result = await _sut.Chat(new ChatRequest("   "));

		Assert.IsType<BadRequestObjectResult>(result);
		Assert.Null(_ragService.LastQuestion);
	}

	[Fact]
	public async Task Chat_WhenRagServiceFails_BubblesException()
	{
		_ragService.Exception = new OllamaUnavailableException("Ollama is down");

		var ex = await Assert.ThrowsAsync<OllamaUnavailableException>(
			() => _sut.Chat(new ChatRequest("How does auth work?")));

		Assert.Equal("Ollama is down", ex.Message);
	}

	private sealed class FakeRagService : IRagService
	{
		public string? LastQuestion { get; private set; }
		public RagResponse Response { get; set; } = new("OK", []);
		public Exception? Exception { get; set; }

		public Task<RagResponse> AskAsync(string question)
		{
			LastQuestion = question;

			if (Exception is not null)
			{
				throw Exception;
			}

			return Task.FromResult(Response);
		}
	}
}
