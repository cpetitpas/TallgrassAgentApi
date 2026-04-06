using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using TallgrassAgentApi.Services;
using TallgrassAgentApi.Models;

namespace TallgrassAgentApi.Tests;

public abstract class TestBase : IClassFixture<WebApplicationFactory<Program>>
{
    protected readonly HttpClient Client;

    protected TestBase(WebApplicationFactory<Program> factory)
    {
        Client = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                var descriptor = services.SingleOrDefault(
                    d => d.ServiceType == typeof(IClaudeService));
                if (descriptor != null)
                    services.Remove(descriptor);

                services.AddScoped<IClaudeService, FakeClaudeService>();
            });
        }).CreateClient();
    }

    protected async Task<HttpResponseMessage> PostAsync(string url, object body)
    {
        var json = JsonSerializer.Serialize(body);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        return await Client.PostAsync(url, content);
    }

    protected async Task<T> PostAndDeserialize<T>(string url, object body)
    {
        var response = await PostAsync(url, body);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<T>();
        Assert.NotNull(result);
        return result!;
    }
}