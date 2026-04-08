using Microsoft.AspNetCore.Mvc.Testing;
using TallgrassAgentApi.Models;

namespace TallgrassAgentApi.Tests;

public class InputValidationTests : TestBase
{
    public InputValidationTests(WebApplicationFactory<Program> factory) : base(factory) {}
    
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
}