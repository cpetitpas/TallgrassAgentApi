namespace TallgrassAgentApi.Models;

// This is what the caller sends us (JSON body of the POST request)
public class AlarmRequest
{
    public string NodeId { get; set; } = string.Empty;      // e.g. "NODE-042"
    public string AlarmType { get; set; } = string.Empty;   // e.g. "HIGH_PRESSURE"
    public double CurrentValue { get; set; }                 // e.g. 847.3
    public double Threshold { get; set; }                    // e.g. 800.0
    public string Unit { get; set; } = string.Empty;         // e.g. "PSI"
    public DateTime Timestamp { get; set; }
}

// This is what we send back
public class AlarmResponse
{
    public string NodeId { get; set; } = string.Empty;
    public string Analysis { get; set; } = string.Empty;     // Claude's response
    public string RecommendedAction { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;     // LOW / MEDIUM / HIGH
}

public class FlowRequest
{
    public string NodeId { get; set; } = string.Empty;
    public string PipelineSegment { get; set; } = string.Empty;  // e.g. "SEG-7A"
    public double FlowRate { get; set; }                          // e.g. 142.7
    public double ExpectedFlowRate { get; set; }                  // e.g. 150.0
    public string Unit { get; set; } = string.Empty;              // e.g. "MMSCFD"
    public string FlowDirection { get; set; } = string.Empty;     // e.g. "FORWARD" / "REVERSE"
    public DateTime Timestamp { get; set; }
}

public class FlowResponse
{
    public string NodeId { get; set; } = string.Empty;
    public string PipelineSegment { get; set; } = string.Empty;
    public string Analysis { get; set; } = string.Empty;
    public string RecommendedAction { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public double Variance { get; set; }                          // % difference from expected
}

public class NodeReading
{
    public string NodeId { get; set; } = string.Empty;
    public string ReadingType { get; set; } = string.Empty;    // "ALARM" or "FLOW"
    public string MetricName { get; set; } = string.Empty;     // e.g. "PRESSURE", "FLOW_RATE"
    public double CurrentValue { get; set; }
    public double ExpectedValue { get; set; }
    public string Unit { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;         // e.g. "NORMAL", "WARNING", "CRITICAL"
    public DateTime Timestamp { get; set; }
}

public class MultiNodeRequest
{
    public string RegionId { get; set; } = string.Empty;       // e.g. "REGION-WEST-4"
    public List<NodeReading> Readings { get; set; } = new();
}

public class MultiNodeResponse
{
    public string RegionId { get; set; } = string.Empty;
    public int TotalNodes { get; set; }
    public int CriticalCount { get; set; }
    public int WarningCount { get; set; }
    public int NormalCount { get; set; }
    public string OverallStatus { get; set; } = string.Empty;  // "NORMAL", "DEGRADED", "CRITICAL"
    public string Summary { get; set; } = string.Empty;
    public string RecommendedAction { get; set; } = string.Empty;
    public List<string> AffectedNodes { get; set; } = new();
}

public class TelemetryEvent
{
    public string EventId { get; set; } = Guid.NewGuid().ToString("N")[..8].ToUpper();
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string NodeId { get; set; } = string.Empty;
    public string PipelineSegment { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;   // "ALARM" | "FLOW" | "MULTINODE"
    public string Severity { get; set; } = string.Empty;    // "LOW" | "MEDIUM" | "HIGH" | "CRITICAL" | "NORMAL" | "DEGRADED"
    public string Analysis { get; set; } = string.Empty;
    public string RecommendedAction { get; set; } = string.Empty;
    public double? CurrentValue { get; set; }
    public double? Threshold { get; set; }
    public string? Unit { get; set; }
    public double? VariancePercent { get; set; }            // flow events only
    public int? TotalNodes { get; set; }                    // multinode events only
    public int? AffectedNodes { get; set; }                 // multinode events only
}