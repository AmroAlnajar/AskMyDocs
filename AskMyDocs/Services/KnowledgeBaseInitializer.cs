namespace askmydocs.Services;

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

        await vectorStoreService.EnsureCollectionAsync();

        var chunks =
            await documentService.GetDocumentChunksAsync();

        await vectorStoreService.StoreAsync(chunks);
    }

    public Task StopAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;
}