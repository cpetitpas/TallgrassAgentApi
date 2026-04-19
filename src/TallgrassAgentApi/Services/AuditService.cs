using System.Collections.Concurrent;
using TallgrassAgentApi.Models;

namespace TallgrassAgentApi.Services;

public class AuditService : IAuditService
{
    private const int MaxEntries = 1000;
    private readonly ConcurrentQueue<AuditEntry> _queue = new();

    public void Record(AuditEntry entry)
    {
        _queue.Enqueue(entry);
        while (_queue.Count > MaxEntries)
            _queue.TryDequeue(out _);
    }

    public IReadOnlyList<AuditEntry> GetRecent(int count = 100)
        => _queue.Reverse().Take(Math.Clamp(count, 1, MaxEntries)).ToList();

    public AuditSummary GetSummary()
    {
        var entries = _queue.ToArray();
        var countsByKind = Enum.GetNames<AuditEntryKind>()
            .ToDictionary(k => k, _ => 0);

        foreach (var group in entries.GroupBy(e => e.Kind.ToString()))
            countsByKind[group.Key] = group.Count();

        return new AuditSummary
        {
            TotalCalls          = entries.Length,
            TotalInputTokens    = entries.Sum(e => e.InputTokens),
            TotalOutputTokens   = entries.Sum(e => e.OutputTokens),
            TotalTokens         = entries.Sum(e => e.TotalTokens),
            AvgLatencyMs        = entries.Length == 0 ? 0
                                    : entries.Average(e => e.ElapsedMs),
            ErrorCount          = entries.Count(e => e.StatusCode >= 400),
            CallsByKind         = countsByKind
        };
    }
}