using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TallgrassAgentApi.Models;
using TallgrassAgentApi.Services;

namespace TallgrassAgentApi.Tests;

public class InputValidationTests : TestBase
{
    private readonly HttpClient _client;
    public InputValidationTests(WebApplicationFactory<Program> factory) : base(factory)
    {
        _client = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                // Remove all IClaudeService registrations and replace with the fake
                services.RemoveAll<IClaudeService>();
                services.AddScoped<IClaudeService, FakeClaudeService>();
            });
        }).CreateClient();
    }
     // Alarm - missing required fields
    [Fact]
    public async Task AlarmAnalyze_EmptyNodeId_ReturnsBadRequest()
    {
        var request = GetTestAlarmRequest();
        request.NodeId = string.Empty;
        var response = await PostAsync("/api/alarm/analyze", request);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AlarmAnalyze_NegativeValue_StillReturnsAnalysis()
    {
        var request = GetTestAlarmRequest();
        request.CurrentValue = -50.0;
        var result = await PostAndDeserialize<AlarmResponse>("/api/alarm/analyze", request);
        Assert.False(string.IsNullOrWhiteSpace(result.Analysis));
    }

    [Fact]
    public async Task AlarmAnalyze_ValueBelowThreshold_ReturnsSeverity()
    {
        var request = GetTestAlarmRequest();
        request.CurrentValue = 100.0;   // well below threshold
        request.Threshold = 800.0;
        var result = await PostAndDeserialize<AlarmResponse>("/api/alarm/analyze", request);
        var valid = new[] { "LOW", "MEDIUM", "HIGH" };
        Assert.Contains(result.Severity, valid);
    }

    // Flow - zero expected value edge case
    [Fact]
    public async Task FlowAnalyze_ZeroExpectedFlowRate_DoesNotThrow()
    {
        var request = GetTestFlowRequest();
        request.ExpectedFlowRate = 0.0;
        var response = await PostAsync("/api/flow/analyze", request);
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }

    // Flow - reverse flow direction
    [Fact]
    public async Task FlowAnalyze_ReverseFlowDirection_ReturnsAnalysis()
    {
        var request = GetTestFlowRequest();
        request.FlowDirection = "REVERSE";
        var result = await PostAndDeserialize<FlowResponse>("/api/flow/analyze", request);
        Assert.False(string.IsNullOrWhiteSpace(result.Analysis));
    }

    // MultiNode - single node
    [Fact]
    public async Task MultiNodeAnalyze_SingleNode_ReturnsValidResponse()
    {
        var request = new MultiNodeRequest
        {
            RegionId = "REGION-TEST",
            Readings = new()
            {
                new() { NodeId = "NODE-001", ReadingType = "ALARM", MetricName = "PRESSURE",
                        CurrentValue = 850.0, ExpectedValue = 850.0, Unit = "PSI",
                        Status = "NORMAL", Timestamp = DateTime.UtcNow }
            }
        };
        var result = await PostAndDeserialize<MultiNodeResponse>("/api/multinode/analyze", request);
        Assert.False(string.IsNullOrWhiteSpace(result.Summary));
    }

    // MultiNode - all nodes normal
    [Fact]
    public async Task MultiNodeAnalyze_AllNormal_ReturnsNormalOrDegraded()
    {
        var request = new MultiNodeRequest
        {
            RegionId = "REGION-TEST",
            Readings = new()
            {
                new() { NodeId = "NODE-001", ReadingType = "ALARM", MetricName = "PRESSURE",
                        CurrentValue = 850.0, ExpectedValue = 850.0, Unit = "PSI",
                        Status = "NORMAL", Timestamp = DateTime.UtcNow },
                new() { NodeId = "NODE-002", ReadingType = "FLOW", MetricName = "FLOW_RATE",
                        CurrentValue = 150.0, ExpectedValue = 150.0, Unit = "MMSCFD",
                        Status = "NORMAL", Timestamp = DateTime.UtcNow }
            }
        };
        var result = await PostAndDeserialize<MultiNodeResponse>("/api/multinode/analyze", request);
        var valid = new[] { "NORMAL", "DEGRADED" };
        Assert.Contains(result.OverallStatus, valid);
    }

     // -------------------------
    // HELPERS
    // -------------------------

    private static AlarmRequest GetTestAlarmRequest() => new()
    {
        NodeId = "NODE-042",
        AlarmType = "HIGH_PRESSURE",
        CurrentValue = 847.3,
        Threshold = 800.0,
        Unit = "PSI",
        Timestamp = DateTime.UtcNow
    };

    private static FlowRequest GetTestFlowRequest() => new()
    {
        NodeId = "NODE-017",
        PipelineSegment = "SEG-7A",
        FlowRate = 118.5,
        ExpectedFlowRate = 150.0,
        Unit = "MMSCFD",
        FlowDirection = "FORWARD",
        Timestamp = DateTime.UtcNow
    };

    private static MultiNodeRequest GetTestMultiNodeRequest() => new()
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