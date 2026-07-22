using System.Net;
using AskMyDocs.API.Services.Documents;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace AskMyDocs.Tests.Health;

public class HealthCheckTests : IClassFixture<HealthCheckTests.ApiFactory>
{
	private readonly HttpClient _client;

	public HealthCheckTests(ApiFactory factory)
	{
		_client = factory.CreateClient();
	}

	[Fact]
	public async Task Health_ReturnsOk()
	{
		var response = await _client.GetAsync("/health");

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
	}

	public sealed class ApiFactory : WebApplicationFactory<Program>
	{
		protected override void ConfigureWebHost(IWebHostBuilder builder)
		{
			builder.UseEnvironment("Development");
			builder.ConfigureTestServices(services =>
			{
				foreach (var descriptor in services
					.Where(service => service.ImplementationType == typeof(KnowledgeBaseInitializer))
					.ToList())
				{
					services.Remove(descriptor);
				}
			});
		}
	}
}
