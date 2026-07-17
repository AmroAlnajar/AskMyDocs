namespace askmydocs.Models;

public record RagResponse(
    string Answer,
    List<SourceReference> Sources);