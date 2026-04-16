using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using TallgrassAgentApi.Models;
using TallgrassAgentApi.Services;
using Xunit;

namespace TallgrassAgentApi.Tests;

// ── Fake HttpMessageHandler ───────────────────────────────────────────────────

/// <summary>
/// Returns a canned sequence of Anthropic-shaped responses.
/// First call returns a tool_use block; second returns end_turn with the answer.
/// </summary>
public class FakeAnthropicHandler : HttpMessageHandler
{
    private int _callCount;

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        _callCount++;
        var body = _callCount == 1 ? FirstResponse() : SecondResponse();
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        });
    }

    private static string FirstResponse() => """
        {
          "stop_reason": "tool_use",
          "content": [
            {
              "type": "tool_use",
              "id":   "tu_001",
              "name": "get_node_spec",
              "input": { "node_id": "NODE-003" }
            }
          ]
        }
        """;

    private static string SecondResponse() => """
        {
          "stop_reason": "end_turn",
          "content": [
            {
              "type": "text",
              "text": "{\"conclusion\":\"Pressure spike is within spec given pipe age.\",\"severity\":\"MEDIUM\",\"recommended_action\":\"Increase monitoring frequency for 48 hours.\"}"
            }
          ]
        }
        """;
}

// ── Tests ─────────────────────────────────────────────────────────────────────

public class InvestigateTests
{
    private InvestigateService BuildService(HttpMessageHandler handler)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Anthropic:ApiKey"] = "test-key"
            })
            .Build();

        var http   = new HttpClient(handler);
        var audit  = new AuditService();
        var logger = NullLogger<InvestigateService>.Instance; // Microsoft.Extensions.Logging.Abstractions
        return new InvestigateService(http, audit, config, logger);
    }

    [Fact]
    public async Task ToolLoop_ExecutesToolThenReturnsConclusion()
    {
        var svc = BuildService(new FakeAnthropicHandler());

        var response = await svc.InvestigateAsync(new InvestigateRequest
        {
            NodeId       = "NODE-003",
            AlarmType    = "HIGH_PRESSURE",
            SensorValue  = 1290,
            Unit         = "PSI"
        });

        Assert.Equal("MEDIUM", response.Severity);
        Assert.Contains("get_node_spec", response.ToolsInvoked);
        Assert.Equal(2, response.Iterations);
        Assert.False(string.IsNullOrEmpty(response.Conclusion));
    }

    [Fact]
    public async Task NodeId_Missing_Returns400()
    {
        await using var app = new WebApplicationFactory<Program>();
        var client = app.CreateClient();

        var resp = await client.PostAsync("/api/alarm/investigate",
            new StringContent(
                """{"nodeId":"","alarmType":"HIGH_PRESSURE","sensorValue":1290,"unit":"PSI"}""",
                Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task PipelineTools_AllToolsReturnValidJson()
    {
        var tools = new[] {
            "get_node_spec", "get_recent_telemetry",
            "get_maintenance_history", "get_adjacent_nodes", "get_pressure_thresholds"
        };
        var input = JsonDocument.Parse("""{"node_id":"NODE-005","count":3}""").RootElement;

        foreach (var tool in tools)
        {
            var result = PipelineTools.Execute(tool, input);
            // Must be parseable JSON — throws if not
            using var doc = JsonDocument.Parse(result);
            Assert.NotNull(doc);
        }
    }

    [Fact(Skip = "Requires Anthropic__ApiKey env var")]
    [Trait("Category", "Integration")]
    public async Task Integration_RealApi_ReturnsValidResponse()
    {
        var config = new ConfigurationBuilder()
            .AddEnvironmentVariables()
            .Build();

        var svc = new InvestigateService(
            new HttpClient(),
            new AuditService(),
            config,
            NullLogger<InvestigateService>.Instance);

        var response = await svc.InvestigateAsync(new InvestigateRequest
        {
            NodeId      = "NODE-003",
            AlarmType   = "HIGH_PRESSURE",
            SensorValue = 1290,
            Unit        = "PSI"
        });

        Assert.NotEmpty(response.Conclusion);
        Assert.Contains(response.Severity, new[] { "LOW", "MEDIUM", "HIGH" });
        Assert.True(response.Iterations >= 1);
    }
}