namespace TallgrassAgentApi.Models;

public record QueueSnapshot
{
    public int  MaxConcurrent { get; init; }
    public int  ActiveCalls   { get; init; }
    public int  WaitingCalls  { get; init; }
    public int  CompletedCalls{ get; init; }
    public int  RejectedCalls { get; init; }
    public bool IsThrottled   => ActiveCalls >= MaxConcurrent;
    public DateTimeOffset SnapshotTime { get; init; } = DateTimeOffset.UtcNow;
}