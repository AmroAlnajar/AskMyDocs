namespace AskMyDocs.API.Services;

public interface IOllamaService
{
	Task<string> ChatAsync(string message);
}