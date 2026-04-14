namespace TallgrassAgentApi.Models;

public record ChatMessage
{
    public string Role    { get; init; } = "";   // "user" or "assistant"
    public string Content { get; init; } = "";
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

public record ChatRequest
{
    public string Message { get; init; } = "";
}

public record ChatResponse
{
    public string       IncidentId { get; init; } = "";
    public string       Reply      { get; init; } = "";
    public int          TurnCount  { get; init; }
    public List<ChatMessage> History { get; init; } = [];
}

public record ConversationState
{
    public string            IncidentId { get; init; } = "";
    public string            NodeId     { get; init; } = "";
    public DateTimeOffset    CreatedAt  { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset    UpdatedAt  { get; set;  } = DateTimeOffset.UtcNow;
    public List<ChatMessage> Messages   { get; init; } = [];
}