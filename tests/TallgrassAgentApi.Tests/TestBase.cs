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

    // -------------------------
    // HELPERS
    // -------------------------

    public static AlarmRequest GetTestAlarmRequest() => new()
    {
        NodeId = "NODE-042",
        AlarmType = "HIGH_PRESSURE",
        CurrentValue = 847.3,
        Threshold = 800.0,
        Unit = "PSI",
        Timestamp = DateTime.UtcNow
    };

    public static FlowRequest GetTestFlowRequest() => new()
    {
        NodeId = "NODE-017",
        PipelineSegment = "SEG-7A",
        FlowRate = 118.5,
        ExpectedFlowRate = 150.0,
        Unit = "MMSCFD",
        FlowDirection = "FORWARD",
        Timestamp = DateTime.UtcNow
    };

    public static MultiNodeRequest GetTestMultiNodeRequest() => new()
    {
        RegionId = "REGION-WEST-4",
        Readings = new()
        {
            new() { NodeId = "NODE-011", ReadingType = "ALARM", MetricName = "PRESSURE",
                    CurrentValue = 798.0, ExpectedValue = 850.0, Unit = "PSI",
                    Status = "WARNING", Timestamp = DateTime.UtcNow },
            new() { NodeId = "NODE-012", ReadingType = "ALARM", MetricName = "PRESSURE",
                    CurrentValue = 741.0, ExpectedValue = 850.0, Unit = "PSI",
                    Status = "CRITICAL", Timestamp = DateTime.UtcNow },
            new() { NodeId = "NODE-013", ReadingType = "ALARM", MetricName = "PRESSURE",
                    CurrentValue = 685.0, ExpectedValue = 850.0, Unit = "PSI",
                    Status = "CRITICAL", Timestamp = DateTime.UtcNow },
            new() { NodeId = "NODE-014", ReadingType = "FLOW", MetricName = "FLOW_RATE",
                    CurrentValue = 98.0, ExpectedValue = 150.0, Unit = "MMSCFD",
                    Status = "WARNING", Timestamp = DateTime.UtcNow },
            new() { NodeId = "NODE-015", ReadingType = "FLOW", MetricName = "FLOW_RATE",
                    CurrentValue = 151.0, ExpectedValue = 150.0, Unit = "MMSCFD",
                    Status = "NORMAL", Timestamp = DateTime.UtcNow }
        }
    };
}