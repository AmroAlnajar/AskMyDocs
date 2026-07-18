namespace AskMyDocs.API.Models;

public record DocumentSearchResult(
	string Content,
	string Source,
	float Score);