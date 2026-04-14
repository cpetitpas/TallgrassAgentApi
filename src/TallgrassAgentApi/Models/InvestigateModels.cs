using System.Text.Json;

namespace TallgrassAgentApi.Models;

public record InvestigateRequest
{
    public string NodeId { get; init; } = "";
    public string AlarmType { get; init; } = "";
    public double SensorValue { get; init; }
    public string Unit { get; init; } = "";
    public string? AdditionalContext { get; init; }
}

public record InvestigateResponse
{
    public string NodeId { get; init; } = "";
    public string Conclusion { get; init; } = "";
    public string Severity { get; init; } = "";
    public string RecommendedAction { get; init; } = "";
    public List<string> ToolsInvoked { get; init; } = [];
    public int Iterations { get; init; }
}

// Internal — represents one tool call Claude made
public record ToolInvocation
{
    public string ToolUseId { get; init; } = "";
    public string ToolName { get; init; } = "";
    public JsonElement Input { get; init; }
}