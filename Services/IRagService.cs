namespace askmydocs.Services;

public interface IRagService
{
	Task<string> AskAsync(string question);
}