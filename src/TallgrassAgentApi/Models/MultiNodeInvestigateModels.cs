namespace TallgrassAgentApi.Models;

public record NodeAlarmInput
{
    public string NodeId      { get; init; } = "";
    public string AlarmType   { get; init; } = "";
    public double SensorValue { get; init; }
    public string Unit        { get; init; } = "";
}

public record MultiNodeInvestigateRequest
{
    public List<NodeAlarmInput> Nodes             { get; init; } = [];
    public string?              RegionContext      { get; init; }  // e.g. "Wamsutter Lateral, Wyoming"
}

public record NodeInvestigationResult
{
    public string       NodeId            { get; init; } = "";
    public string       Conclusion        { get; init; } = "";
    public string       Severity          { get; init; } = "";
    public string       RecommendedAction { get; init; } = "";
    public List<string> ToolsInvoked      { get; init; } = [];
    public int          Iterations        { get; init; }
}

public record MultiNodeInvestigateResponse
{
    public string                      RootCauseHypothesis { get; init; } = "";
    public string                      OverallSeverity     { get; init; } = "";
    public string                      RecommendedAction   { get; init; } = "";
    public string                      CorrelationSummary  { get; init; } = "";
    public List<NodeInvestigationResult> NodeResults       { get; init; } = [];
    public int                         TotalIterations     { get; init; }
    public List<string>                AffectedNodes       { get; init; } = [];
}