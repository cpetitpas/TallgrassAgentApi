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

public class SeverityCorrelationTests : TestBase
{
    public SeverityCorrelationTests(WebApplicationFactory<Program> factory) : base(factory) { }

    // ALARM SEVERITY
    [Fact]
    public async Task AlarmAnalyze_ValueAtExactlyThreshold_ReturnsLowSeverity()
    {
        // Exactly at threshold — fake returns LOW (not >= 1.1x)
        var request = GetAlarmRequest(currentValue: 800.0, threshold: 800.0);
        var result = await PostAndDeserialize<AlarmResponse>("/api/alarm/analyze", request);
        Assert.Equal("LOW", result.Severity);
    }

    [Fact]
    public async Task AlarmAnalyze_TenPercentOver_ReturnsMediumSeverity()
    {
        // Exactly 1.1x threshold — fake returns MEDIUM
        var request = GetAlarmRequest(currentValue: 881.0, threshold: 800.0);
        var result = await PostAndDeserialize<AlarmResponse>("/api/alarm/analyze", request);
        Assert.Equal("MEDIUM", result.Severity);
    }

    [Fact]
    public async Task AlarmAnalyze_FiftyPercentOver_ReturnsHighSeverity()
    {
        // Exactly 1.5x threshold — fake returns HIGH
        var request = GetAlarmRequest(currentValue: 1200.0, threshold: 800.0);
        var result = await PostAndDeserialize<AlarmResponse>("/api/alarm/analyze", request);
        Assert.Equal("HIGH", result.Severity);
    }

    [Fact]
    public async Task AlarmAnalyze_WellBelowThreshold_ReturnsLowSeverity()
    {
        var request = GetAlarmRequest(currentValue: 100.0, threshold: 800.0);
        var result = await PostAndDeserialize<AlarmResponse>("/api/alarm/analyze", request);
        Assert.Equal("LOW", result.Severity);
    }

    // FLOW SEVERITY
    [Fact]
    public async Task FlowAnalyze_SmallVariance_ReturnsLowSeverity()
    {
        // 1.3% variance — fake returns LOW (not > 15%)
        var request = GetFlowRequest(flowRate: 148.0, expectedRate: 150.0);
        var result = await PostAndDeserialize<FlowResponse>("/api/flow/analyze", request);
        Assert.Equal("LOW", result.Severity);
    }

    [Fact]
    public async Task FlowAnalyze_SixteenPercentVariance_ReturnsMediumSeverity()
    {
        // 16% variance — fake returns MEDIUM (> 15% but not > 30%)
        var request = GetFlowRequest(flowRate: 126.0, expectedRate: 150.0);
        var result = await PostAndDeserialize<FlowResponse>("/api/flow/analyze", request);
        Assert.Equal("MEDIUM", result.Severity);
    }

    [Fact]
    public async Task FlowAnalyze_ThirtyOnePercentVariance_ReturnsHighSeverity()
    {
        // 31% variance — fake returns HIGH (> 30%)
        var request = GetFlowRequest(flowRate: 103.0, expectedRate: 150.0);
        var result = await PostAndDeserialize<FlowResponse>("/api/flow/analyze", request);
        Assert.Equal("HIGH", result.Severity);
    }

    [Fact]
    public async Task FlowAnalyze_NegativeVariance_SeverityStillValid()
    {
        // Flow above expected — variance is negative but abs value drives severity
        var request = GetFlowRequest(flowRate: 200.0, expectedRate: 150.0);
        var result = await PostAndDeserialize<FlowResponse>("/api/flow/analyze", request);
        var valid = new[] { "LOW", "MEDIUM", "HIGH" };
        Assert.Contains(result.Severity, valid);
    }

    // MULTI-NODE OVERALL STATUS CORRELATION
    [Fact]
    public async Task MultiNodeAnalyze_NoCriticalOrWarning_ReturnsNormal()
    {
        var request = AllNormalRequest();
        var result = await PostAndDeserialize<MultiNodeResponse>("/api/multinode/analyze", request);
        Assert.Equal("NORMAL", result.OverallStatus);
    }

    [Fact]
    public async Task MultiNodeAnalyze_OneWarningNoCritical_ReturnsDegraded()
    {
        var request = OneWarningRequest();
        var result = await PostAndDeserialize<MultiNodeResponse>("/api/multinode/analyze", request);
        Assert.Equal("DEGRADED", result.OverallStatus);
    }

    [Fact]
    public async Task MultiNodeAnalyze_OneCritical_ReturnsCritical()
    {
        var request = OneCriticalRequest();
        var result = await PostAndDeserialize<MultiNodeResponse>("/api/multinode/analyze", request);
        Assert.Equal("CRITICAL", result.OverallStatus);
    }

    [Fact]
    public async Task MultiNodeAnalyze_MixedCriticalAndWarning_ReturnsCritical()
    {
        // Critical takes precedence over warning
        var request = new MultiNodeRequest
        {
            RegionId = "REGION-TEST",
            Readings = new()
            {
                new() { NodeId = "NODE-001", ReadingType = "ALARM", MetricName = "PRESSURE",
                        CurrentValue = 1200.0, ExpectedValue = 800.0, Unit = "PSI",
                        Status = "CRITICAL", Timestamp = DateTime.UtcNow },
                new() { NodeId = "NODE-002", ReadingType = "FLOW", MetricName = "FLOW_RATE",
                        CurrentValue = 120.0, ExpectedValue = 150.0, Unit = "MMSCFD",
                        Status = "WARNING", Timestamp = DateTime.UtcNow }
            }
        };
        var result = await PostAndDeserialize<MultiNodeResponse>("/api/multinode/analyze", request);
        Assert.Equal("CRITICAL", result.OverallStatus);
    }

    // -------------------------
    // HELPERS
    // -------------------------

    private static AlarmRequest GetAlarmRequest(double currentValue, double threshold) => new()
    {
        NodeId = "NODE-TEST",
        AlarmType = "HIGH_PRESSURE",
        CurrentValue = currentValue,
        Threshold = threshold,
        Unit = "PSI",
        Timestamp = DateTime.UtcNow
    };

    private static FlowRequest GetFlowRequest(double flowRate, double expectedRate) => new()
    {
        NodeId = "NODE-TEST",
        PipelineSegment = "SEG-TEST",
        FlowRate = flowRate,
        ExpectedFlowRate = expectedRate,
        Unit = "MMSCFD",
        FlowDirection = "FORWARD",
        Timestamp = DateTime.UtcNow
    };

    private static MultiNodeRequest AllNormalRequest() => new()
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

    private static MultiNodeRequest OneWarningRequest() => new()
    {
        RegionId = "REGION-TEST",
        Readings = new()
        {
            new() { NodeId = "NODE-001", ReadingType = "ALARM", MetricName = "PRESSURE",
                    CurrentValue = 850.0, ExpectedValue = 850.0, Unit = "PSI",
                    Status = "NORMAL", Timestamp = DateTime.UtcNow },
            new() { NodeId = "NODE-002", ReadingType = "FLOW", MetricName = "FLOW_RATE",
                    CurrentValue = 120.0, ExpectedValue = 150.0, Unit = "MMSCFD",
                    Status = "WARNING", Timestamp = DateTime.UtcNow }
        }
    };

    private static MultiNodeRequest OneCriticalRequest() => new()
    {
        RegionId = "REGION-TEST",
        Readings = new()
        {
            new() { NodeId = "NODE-001", ReadingType = "ALARM", MetricName = "PRESSURE",
                    CurrentValue = 1200.0, ExpectedValue = 800.0, Unit = "PSI",
                    Status = "CRITICAL", Timestamp = DateTime.UtcNow },
            new() { NodeId = "NODE-002", ReadingType = "FLOW", MetricName = "FLOW_RATE",
                    CurrentValue = 150.0, ExpectedValue = 150.0, Unit = "MMSCFD",
                    Status = "NORMAL", Timestamp = DateTime.UtcNow }
        }
    };
}