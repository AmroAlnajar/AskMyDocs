namespace AskMyDocs.API.Services.AI;

public sealed class OllamaUnavailableException : Exception
{
	public OllamaUnavailableException(string message, Exception? innerException = null)
		: base(message, innerException)
	{
	}
}
