using askmydocs.Models;

namespace askmydocs.Services;

public interface IRagService
{
	Task<RagResponse> AskAsync(string question);
}