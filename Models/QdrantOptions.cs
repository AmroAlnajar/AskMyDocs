namespace askmydocs.Models;

public class QdrantOptions
{
	public const string SectionName = "Qdrant";

	public string Host { get; set; } = "localhost";
	public int Port { get; set; } = 6334;
}