using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using TallgrassAgentApi.Models;
using TallgrassAgentApi.Services;

namespace TallgrassAgentApi.Tests;

public class EndpointTests : TestBase
{
    public EndpointTests(WebApplicationFactory<Program> factory) : base(factory) {}
    
    // -------------------------
    // ALARM ENDPOINT TESTS
    // -------------------------

    [Fact]
    public async Task AlarmAnalyze_ReturnsSuccess()
    {
        var request = GetTestAlarmRequest();
        var response = await PostAsync("/api/alarm/analyze", request);
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AlarmAnalyze_NodeId_IsPopulated()
    {
        var request = GetTestAlarmRequest();
        var result = await PostAndDeserialize<AlarmResponse>("/api/alarm/analyze", request);
        Assert.False(string.IsNullOrWhiteSpace(result.NodeId), "NodeId should not be empty");
    }

    [Fact]
    public async Task AlarmAnalyze_Analysis_IsPopulated()
    {
        var request = GetTestAlarmRequest();
        var result = await PostAndDeserialize<AlarmResponse>("/api/alarm/analyze", request);
        Assert.False(string.IsNullOrWhiteSpace(result.Analysis), "Analysis should not be empty");
    }

    [Fact]
    public async Task AlarmAnalyze_RecommendedAction_IsPopulated()
    {
        var request = GetTestAlarmRequest();
        var result = await PostAndDeserialize<AlarmResponse>("/api/alarm/analyze", request);
        Assert.False(string.IsNullOrWhiteSpace(result.RecommendedAction), "RecommendedAction should not be empty");
    }

    [Fact]
    public async Task AlarmAnalyze_Severity_IsValidValue()
    {
        var request = GetTestAlarmRequest();
        var result = await PostAndDeserialize<AlarmResponse>("/api/alarm/analyze", request);
        var valid = new[] { "LOW", "MEDIUM", "HIGH" };
        Assert.Contains(result.Severity, valid);
    }

    // -------------------------
    // FLOW ENDPOINT TESTS
    // -------------------------

    [Fact]
    public async Task FlowAnalyze_ReturnsSuccess()
    {
        var request = GetTestFlowRequest();
        var response = await PostAsync("/api/flow/analyze", request);
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task FlowAnalyze_NodeId_IsPopulated()
    {
        var request = GetTestFlowRequest();
        var result = await PostAndDeserialize<FlowResponse>("/api/flow/analyze", request);
        Assert.False(string.IsNullOrWhiteSpace(result.NodeId), "NodeId should not be empty");
    }

    [Fact]
    public async Task FlowAnalyze_PipelineSegment_IsPopulated()
    {
        var request = GetTestFlowRequest();
        var result = await PostAndDeserialize<FlowResponse>("/api/flow/analyze", request);
        Assert.False(string.IsNullOrWhiteSpace(result.PipelineSegment), "PipelineSegment should not be empty");
    }

    [Fact]
    public async Task FlowAnalyze_Analysis_IsPopulated()
    {
        var request = GetTestFlowRequest();
        var result = await PostAndDeserialize<FlowResponse>("/api/flow/analyze", request);
        Assert.False(string.IsNullOrWhiteSpace(result.Analysis), "Analysis should not be empty");
    }

    [Fact]
    public async Task FlowAnalyze_RecommendedAction_IsPopulated()
    {
        var request = GetTestFlowRequest();
        var result = await PostAndDeserialize<FlowResponse>("/api/flow/analyze", request);
        Assert.False(string.IsNullOrWhiteSpace(result.RecommendedAction), "RecommendedAction should not be empty");
    }

    [Fact]
    public async Task FlowAnalyze_Severity_IsValidValue()
    {
        var request = GetTestFlowRequest();
        var result = await PostAndDeserialize<FlowResponse>("/api/flow/analyze", request);
        var valid = new[] { "LOW", "MEDIUM", "HIGH" };
        Assert.Contains(result.Severity, valid);
    }

    [Fact]
    public async Task FlowAnalyze_Variance_IsCalculated()
    {
        var request = GetTestFlowRequest();
        var result = await PostAndDeserialize<FlowResponse>("/api/flow/analyze", request);
        Assert.NotEqual(0.0, result.Variance);
    }

    // -------------------------
    // MULTI-NODE ENDPOINT TESTS
    // -------------------------

    [Fact]
    public async Task MultiNodeAnalyze_ReturnsSuccess()
    {
        var request = GetTestMultiNodeRequest();
        var response = await PostAsync("/api/multinode/analyze", request);
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task MultiNodeAnalyze_RegionId_IsPopulated()
    {
        var request = GetTestMultiNodeRequest();
        var result = await PostAndDeserialize<MultiNodeResponse>("/api/multinode/analyze", request);
        Assert.False(string.IsNullOrWhiteSpace(result.RegionId), "RegionId should not be empty");
    }

    [Fact]
    public async Task MultiNodeAnalyze_Summary_IsPopulated()
    {
        var request = GetTestMultiNodeRequest();
        var result = await PostAndDeserialize<MultiNodeResponse>("/api/multinode/analyze", request);
        Assert.False(string.IsNullOrWhiteSpace(result.Summary), "Summary should not be empty");
    }

    [Fact]
    public async Task MultiNodeAnalyze_RecommendedAction_IsPopulated()
    {
        var request = GetTestMultiNodeRequest();
        var result = await PostAndDeserialize<MultiNodeResponse>("/api/multinode/analyze", request);
        Assert.False(string.IsNullOrWhiteSpace(result.RecommendedAction), "RecommendedAction should not be empty");
    }

    [Fact]
    public async Task MultiNodeAnalyze_OverallStatus_IsValidValue()
    {
        var request = GetTestMultiNodeRequest();
        var result = await PostAndDeserialize<MultiNodeResponse>("/api/multinode/analyze", request);
        var valid = new[] { "NORMAL", "DEGRADED", "CRITICAL" };
        Assert.Contains(result.OverallStatus, valid);
    }

    [Fact]
    public async Task MultiNodeAnalyze_NodeCounts_AddUpToTotal()
    {
        var request = GetTestMultiNodeRequest();
        var result = await PostAndDeserialize<MultiNodeResponse>("/api/multinode/analyze", request);
        var countSum = result.CriticalCount + result.WarningCount + result.NormalCount;
        Assert.Equal(result.TotalNodes, countSum);
    }

    [Fact]
    public async Task MultiNodeAnalyze_AffectedNodes_IsNotNull()
    {
        var request = GetTestMultiNodeRequest();
        var result = await PostAndDeserialize<MultiNodeResponse>("/api/multinode/analyze", request);
        Assert.NotNull(result.AffectedNodes);
    }

    [Fact]
    public async Task MultiNodeAnalyze_EmptyReadings_ReturnsBadRequest()
    {
        var request = new MultiNodeRequest { RegionId = "REGION-TEST", Readings = new() };
        var response = await PostAsync("/api/multinode/analyze", request);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
    }
}