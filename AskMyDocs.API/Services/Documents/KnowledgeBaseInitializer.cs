using AskMyDocs.API.Services.VectorStore;

namespace AskMyDocs.API.Services.Documents;

public class KnowledgeBaseInitializer(
    IServiceScopeFactory scopeFactory) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();

        var documentService =
            scope.ServiceProvider.GetRequiredService<IDocumentService>();

        var vectorStoreService =
            scope.ServiceProvider.GetRequiredService<IVectorStoreService>();

        await vectorStoreService.EnsureCollectionAsync(cancellationToken);

        var chunks =
            await documentService.GetDocumentChunksAsync(cancellationToken);

        await vectorStoreService.StoreAsync(chunks, cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;
}