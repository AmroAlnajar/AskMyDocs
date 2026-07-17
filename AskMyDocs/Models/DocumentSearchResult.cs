namespace askmydocs.Models;

public record DocumentSearchResult(
	string Content,
	string Source,
	float Score);