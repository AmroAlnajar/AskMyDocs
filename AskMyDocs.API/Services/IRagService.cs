using AskMyDocs.API.Models;

namespace AskMyDocs.API.Services;

public interface IRagService
{
	Task<RagResponse> AskAsync(string question);
}