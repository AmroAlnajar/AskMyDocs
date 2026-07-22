using AskMyDocs.API.Models;
using AskMyDocs.API.Services.AI;
using AskMyDocs.API.Services.Documents;
using AskMyDocs.API.Services.RAG;
using AskMyDocs.API.Services.VectorStore;
using Microsoft.AspNetCore.Diagnostics;
using Qdrant.Client;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();

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

// Qdrant configuration
var qdrantSection = builder.Configuration.GetSection(QdrantOptions.SectionName);
builder.Services.Configure<QdrantOptions>(qdrantSection);

var qdrantOptions = qdrantSection.Get<QdrantOptions>()
	?? throw new InvalidOperationException("Qdrant configuration is missing.");

builder.Services.AddSingleton(
	new QdrantClient(
		qdrantOptions.Host,
		qdrantOptions.Port));

// Application services
builder.Services.AddScoped<IDocumentService, DocumentService>();
builder.Services.AddScoped<IVectorStoreService, VectorStoreService>();
builder.Services.AddScoped<IRagService, RagService>();
builder.Services.AddHostedService<KnowledgeBaseInitializer>();

var app = builder.Build();

app.UseExceptionHandler(errorApp =>
{
	errorApp.Run(async context =>
	{
		var error = context.Features.Get<IExceptionHandlerFeature>()?.Error;
		var ollamaDown = error is OllamaUnavailableException;

		context.Response.StatusCode = ollamaDown
			? StatusCodes.Status503ServiceUnavailable
			: StatusCodes.Status500InternalServerError;

		await Results.Problem(
			title: ollamaDown ? "Ollama is unavailable" : "An error occurred",
			statusCode: context.Response.StatusCode).ExecuteAsync(context);
	});
});

if (app.Environment.IsDevelopment())
{
	app.MapOpenApi();
}

app.UseHttpsRedirection();
app.MapControllers();
app.MapHealthChecks("/health");

app.Run();