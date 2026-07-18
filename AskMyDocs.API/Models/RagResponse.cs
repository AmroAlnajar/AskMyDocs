namespace AskMyDocs.API.Models;

public record RagResponse(
    string Answer,
    List<SourceReference> Sources);