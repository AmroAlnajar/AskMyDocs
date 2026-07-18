namespace AskMyDocs.API.Services;

public sealed class OllamaUnavailableException : Exception
{
	public OllamaUnavailableException(string message, Exception? innerException = null)
		: base(message, innerException)
	{
	}
}
