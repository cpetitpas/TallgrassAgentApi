namespace TallgrassAgentApi.Models;

public enum AuditEntryKind { Alarm, Flow, MultiNode, Investigate, Chat }

public record AuditEntry
{
    public string         Id           { get; init; } = Guid.NewGuid().ToString("N")[..12];
    public DateTimeOffset Timestamp    { get; init; } = DateTimeOffset.UtcNow;
    public AuditEntryKind Kind         { get; init; }
    public string         NodeId       { get; init; } = "";
    public string?        IncidentId   { get; init; }          // Chat / Investigate only

    // Prompt fingerprint — SHA-256 of the user content, first 16 hex chars
    public string         PromptHash   { get; init; } = "";

    // Token usage direct from the Anthropic response
    public int            InputTokens  { get; init; }
    public int            OutputTokens { get; init; }
    public int            TotalTokens  => InputTokens + OutputTokens;

    // Latency
    public long           ElapsedMs    { get; init; }

    // Model used
    public string         Model        { get; init; } = "";

    // Whether Claude called any tools this turn (Investigate / Chat)
    public bool           UsedTools    { get; init; }

    // HTTP status from Anthropic
    public int            StatusCode   { get; init; }
}

public record AuditSummary
{
    public int    TotalCalls     { get; init; }
    public int    TotalInputTokens  { get; init; }
    public int    TotalOutputTokens { get; init; }
    public int    TotalTokens    { get; init; }
    public double AvgLatencyMs   { get; init; }
    public int    ErrorCount     { get; init; }
    public Dictionary<string, int> CallsByKind { get; init; } = [];
}