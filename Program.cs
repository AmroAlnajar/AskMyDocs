using askmydocs.Models;
using askmydocs.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();

var ollamaSection = builder.Configuration.GetSection(OllamaOptions.SectionName);
builder.Services.Configure<OllamaOptions>(ollamaSection);

var ollamaOptions = ollamaSection.Get<OllamaOptions>()
	?? throw new InvalidOperationException("Ollama configuration is missing.");

builder.Services.AddHttpClient<IOllamaService, OllamaService>(client =>
{
	client.BaseAddress = new Uri(ollamaOptions.BaseUrl);
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
	app.MapOpenApi();
}

app.UseHttpsRedirection();
app.MapControllers();

app.Run();
