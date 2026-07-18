namespace AskMyDocs.API.Services.AI;

public interface IOllamaService
{
	Task<string> ChatAsync(string message);
}