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

/// <summary>
/// Fake InvestigateService — returns deterministic per-node results without
/// hitting the real API. Severity cycles HIGH → MEDIUM → LOW by node index.
/// </summary>
public class FakeInvestigateService : IInvestigateService
{
    private static readonly string[] Severities = ["HIGH", "MEDIUM", "LOW"];

    public Task<InvestigateResponse> InvestigateAsync(
        InvestigateRequest request, CancellationToken cancellationToken = default)
    {
        // Derive index from the trailing number in the NodeId so the result is
        // deterministic regardless of the order parallel invocations complete.
        var numStr = System.Text.RegularExpressions.Regex.Match(request.NodeId, @"\d+$").Value;
        var idx    = int.TryParse(numStr, out var n) ? n : 0;
        return Task.FromResult(new InvestigateResponse
        {
            NodeId            = request.NodeId,
            Conclusion        = $"Test conclusion for {request.NodeId}.",
            Severity          = Severities[idx % 3],
            RecommendedAction = $"Test action for {request.NodeId}.",
            ToolsInvoked      = ["get_node_spec", "get_pressure_thresholds"],
            Iterations        = 2
        });
    }
}

/// <summary>
/// Fake HTTP handler for the synthesis call.
/// </summary>
public class FakeSynthesisHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = """
            {
              "content": [{
                "type": "text",
                "text": "{\"root_cause_hypothesis\":\"Regional pressure surge on Wamsutter Lateral.\",\"overall_severity\":\"HIGH\",\"recommended_action\":\"Reduce inlet pressure at compressor station CS-07.\",\"correlation_summary\":\"All three nodes show simultaneous pressure elevation consistent with a compressor overpressure event upstream.\"}"
              }],
              "stop_reason": "end_turn",
              "usage": { "input_tokens": 420, "output_tokens": 95 }
            }
            """;
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        });
    }
}

public class MultiNodeInvestigateTests
{
    private static (MultiNodeInvestigateService svc, FakeInvestigateService fakeSingle)
        Build(HttpMessageHandler? handler = null)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Anthropic:ApiKey"]             = "test",
                ["ClaudeThrottle:MaxConcurrent"] = "3",
                ["ClaudeThrottle:MaxWaitMs"]     = "5000"
            })
            .Build();

        var fakeSingle = new FakeInvestigateService();
        var throttle   = new ClaudeThrottle(config);
        var audit      = new AuditService();
        var http       = new HttpClient(handler ?? new FakeSynthesisHandler());
        var logger     = NullLogger<MultiNodeInvestigateService>.Instance;

        var svc = new MultiNodeInvestigateService(
            fakeSingle, http, config, throttle, audit, logger);

        return (svc, fakeSingle);
    }

    private static MultiNodeInvestigateRequest ThreeNodeRequest() => new()
    {
        Nodes = [
            new NodeAlarmInput { NodeId = "NODE-002", AlarmType = "HIGH_PRESSURE", SensorValue = 1310, Unit = "PSI" },
            new NodeAlarmInput { NodeId = "NODE-003", AlarmType = "HIGH_PRESSURE", SensorValue = 1290, Unit = "PSI" },
            new NodeAlarmInput { NodeId = "NODE-004", AlarmType = "HIGH_PRESSURE", SensorValue = 1275, Unit = "PSI" }
        ],
        RegionContext = "Wamsutter Lateral, Wyoming"
    };

    [Fact]
    public async Task InvestigatesAllNodes_AndReturnsSynthesis()
    {
        var (svc, _) = Build();
        var result   = await svc.InvestigateAsync(ThreeNodeRequest());

        Assert.Equal(3, result.NodeResults.Count);
        Assert.NotEmpty(result.RootCauseHypothesis);
        Assert.NotEmpty(result.CorrelationSummary);
        Assert.Contains(result.OverallSeverity, new[] { "LOW", "MEDIUM", "HIGH", "UNKNOWN" });
    }

    [Fact]
    public async Task AffectedNodes_ExcludesLowSeverity()
    {
        var (svc, _) = Build();
        var result   = await svc.InvestigateAsync(ThreeNodeRequest());

        // FakeInvestigateService maps severity by node number (num % 3):
        // NODE-002 → idx 2 → LOW  (excluded)
        // NODE-003 → idx 0 → HIGH (included)
        // NODE-004 → idx 1 → MEDIUM (included)
        Assert.DoesNotContain("NODE-002", result.AffectedNodes);
        Assert.Contains("NODE-003", result.AffectedNodes);
    }

    [Fact]
    public async Task TotalIterations_SumsAcrossNodes()
    {
        var (svc, _) = Build();
        var result   = await svc.InvestigateAsync(ThreeNodeRequest());

        // FakeInvestigateService returns 2 iterations per node, 3 nodes = 6
        Assert.Equal(6, result.TotalIterations);
    }

    [Fact]
    public async Task Endpoint_ReturnsBadRequest_ForSingleNode()
    {
        await using var app = new WebApplicationFactory<Program>();
        var client = app.CreateClient();

        var resp = await client.PostAsync(
            "/api/alarm/investigate/multinode",
            new StringContent(
                """{"nodes":[{"nodeId":"NODE-001","alarmType":"HIGH_PRESSURE","sensorValue":1290,"unit":"PSI"}]}""",
                Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Endpoint_ReturnsBadRequest_ForMoreThan10Nodes()
    {
        await using var app = new WebApplicationFactory<Program>();
        var client = app.CreateClient();

        var nodes = Enumerable.Range(1, 11).Select(i => new
        {
            nodeId = $"NODE-{i:D3}", alarmType = "HIGH_PRESSURE",
            sensorValue = 1290, unit = "PSI"
        });

        var resp = await client.PostAsync(
            "/api/alarm/investigate/multinode",
            new StringContent(
                JsonSerializer.Serialize(new { nodes }),
                Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact(Skip = "Requires Anthropic__ApiKey env var")]
    [Trait("Category", "Integration")]
    public async Task Integration_RealApi_ThreeNodes()
    {
        var config = new ConfigurationBuilder()
            .AddEnvironmentVariables()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ClaudeThrottle:MaxConcurrent"] = "3",
                ["ClaudeThrottle:MaxWaitMs"]     = "15000"
            })
            .Build();

        var realInvestigate = new InvestigateService(
            new HttpClient(),
            new ClaudeThrottle(config),
            new AuditService(),
            config,
            NullLogger<InvestigateService>.Instance);

        var svc = new MultiNodeInvestigateService(
            realInvestigate,
            new HttpClient(),
            config,
            new ClaudeThrottle(config),
            new AuditService(),
            NullLogger<MultiNodeInvestigateService>.Instance);

        var result = await svc.InvestigateAsync(ThreeNodeRequest());

        Assert.Equal(3, result.NodeResults.Count);
        Assert.NotEmpty(result.RootCauseHypothesis);
        Assert.Contains(result.OverallSeverity, new[] { "LOW", "MEDIUM", "HIGH" });
        Assert.True(result.TotalIterations >= 3);
    }
}