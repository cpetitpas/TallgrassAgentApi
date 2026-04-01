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