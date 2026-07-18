using AskMyDocs.API.Models;

namespace AskMyDocs.API.Services.RAG;

public interface IRagService
{
	Task<RagResponse> AskAsync(string question);
}