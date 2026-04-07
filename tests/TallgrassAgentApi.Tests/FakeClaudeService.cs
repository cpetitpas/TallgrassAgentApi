using System.Text.Json;
using TallgrassAgentApi.Models;
using TallgrassAgentApi.Services;

namespace TallgrassAgentApi.Tests;

public class FakeClaudeService : IClaudeService
{
    public Task<string> AnalyzeAlarmAsync(AlarmRequest alarm)
    {
        var response = new
        {
            analysis = $"Pressure at {alarm.NodeId} is reading {alarm.CurrentValue} {alarm.Unit}, which exceeds the threshold of {alarm.Threshold} {alarm.Unit}.",
            recommended_action = "Dispatch field technician to inspect the node immediately.",
            severity = alarm.CurrentValue >= alarm.Threshold * 1.5 ? "HIGH"
                     : alarm.CurrentValue >= alarm.Threshold * 1.1 ? "MEDIUM"
                     : "LOW"
        };
        return Task.FromResult(JsonSerializer.Serialize(response));
    }

    public Task<string> AnalyzeFlowAsync(FlowRequest flow)
    {
        var variance = flow.ExpectedFlowRate != 0
            ? ((flow.FlowRate - flow.ExpectedFlowRate) / flow.ExpectedFlowRate) * 100
            : 0;
        var response = new
        {
            analysis = $"Flow rate at {flow.NodeId} on segment {flow.PipelineSegment} is {flow.FlowRate} {flow.Unit}, " +
                       $"a variance of {Math.Abs(variance):F1}% from expected.",
            recommended_action = Math.Abs(variance) > 20
                ? "Investigate potential blockage or leak on this segment."
                : "Continue monitoring. No immediate action required.",
            severity = Math.Abs(variance) > 30 ? "HIGH"
                     : Math.Abs(variance) > 15 ? "MEDIUM"
                     : "LOW"
        };
        return Task.FromResult(JsonSerializer.Serialize(response));
    }

    public Task<string> AnalyzeMultiNodeAsync(MultiNodeRequest request)
    {
        var criticalNodes = request.Readings
            .Where(r => r.Status == "CRITICAL")
            .Select(r => r.NodeId)
            .ToList();

        var warningNodes = request.Readings
            .Where(r => r.Status == "WARNING")
            .Select(r => r.NodeId)
            .ToList();

        var overallStatus = criticalNodes.Count > 0 ? "CRITICAL"
                          : warningNodes.Count > 0 ? "DEGRADED"
                          : "NORMAL";

        var affectedNodes = criticalNodes.Concat(warningNodes).ToList();

        var pressureNodes = request.Readings
            .Where(r => r.MetricName == "PRESSURE" && r.Status != "NORMAL")
            .Select(r => r.NodeId)
            .ToList();

        var leakNote = pressureNodes.Count >= 2
            ? $" A progressive pressure drop across sequential nodes suggests a possible leak between {pressureNodes.First()} and {pressureNodes.Last()}."
            : "";

        var response = new
        {
            overall_status = overallStatus,
            summary = $"Region {request.RegionId} has {criticalNodes.Count} critical and " +
                      $"{warningNodes.Count} warning nodes out of {request.Readings.Count} total.{leakNote}",
            recommended_action = criticalNodes.Count > 0
                ? $"Immediate inspection required at nodes: {string.Join(", ", criticalNodes)}."
                : "Monitor warning nodes and schedule inspection.",
            affected_nodes = affectedNodes
        };
        return Task.FromResult(JsonSerializer.Serialize(response));
    }
}