namespace askmydocs.Services;

public interface IOllamaService
{
	Task<string> ChatAsync(string message);
}