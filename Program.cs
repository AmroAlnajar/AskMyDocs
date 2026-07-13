using askmydocs.Models;
using askmydocs.Services;
using Qdrant.Client;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();

var ollamaSection = builder.Configuration.GetSection(OllamaOptions.SectionName);
builder.Services.Configure<OllamaOptions>(ollamaSection);

var ollamaOptions = ollamaSection.Get<OllamaOptions>()
	?? throw new InvalidOperationException("Ollama configuration is missing.");

builder.Services.AddHttpClient<IEmbeddingService, EmbeddingService>(client =>
{
	client.BaseAddress = new Uri(ollamaOptions.BaseUrl);
});

builder.Services.AddHttpClient<IOllamaService, OllamaService>(client =>
{
	client.BaseAddress = new Uri(ollamaOptions.BaseUrl);
});

builder.Services.AddScoped<IDocumentService, DocumentService>();

builder.Services.AddSingleton(
	new QdrantClient("localhost", 6334));

builder.Services.AddScoped<IVectorStoreService, VectorStoreService>();

builder.Services.AddScoped<IRagService, RagService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
	app.MapOpenApi();
}

app.UseHttpsRedirection();
app.MapControllers();

app.Run();
