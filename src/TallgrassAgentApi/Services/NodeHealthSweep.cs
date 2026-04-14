// Services/NodeHealthSweep.cs
using TallgrassAgentApi.Models;

namespace TallgrassAgentApi.Services;

/// <summary>
/// Background service that runs every SweepIntervalSeconds and checks each
/// registered node for a missed heartbeat. HeartbeatIntervalSeconds defines
/// the staleness window used to decide whether a heartbeat is overdue.
/// Nodes that miss intervals are transitioned HEALTHY → DEGRADED → OFFLINE
/// and a TelemetryEvent is published to the SSE channel so the dashboard
/// reflects the state change.
///
/// Configuration (appsettings.json):
///   "NodeHealth": {
///     "HeartbeatIntervalSeconds": 30,
///     "SweepIntervalSeconds": 15
///   }
/// </summary>
public class NodeHealthSweep : BackgroundService
{
    private readonly NodeHealthRegistry _registry;
    private readonly TelemetryChannel _channel;
    private readonly ILogger<NodeHealthSweep> _logger;
    private readonly TimeSpan _heartbeatInterval;
    private readonly TimeSpan _sweepInterval;

    public NodeHealthSweep(
        NodeHealthRegistry registry,
        TelemetryChannel channel,
        IConfiguration config,
        ILogger<NodeHealthSweep> logger)
    {
        _registry = registry;
        _channel  = channel;
        _logger   = logger;

        _heartbeatInterval = TimeSpan.FromSeconds(
            config.GetValue("NodeHealth:HeartbeatIntervalSeconds", 30));
        _sweepInterval = TimeSpan.FromSeconds(
            config.GetValue("NodeHealth:SweepIntervalSeconds", 15));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "NodeHealthSweep started. Heartbeat window: {Window}s, sweep every {Sweep}s",
            _heartbeatInterval.TotalSeconds, _sweepInterval.TotalSeconds);

        using var timer = new PeriodicTimer(_sweepInterval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            var cutoff = DateTime.UtcNow - _heartbeatInterval;

            foreach (var (nodeId, entry) in _registry.All)
            {
                string? before = null;
                string? after = null;
                DateTime? lastHeartbeat = null;
                string? pipelineSegment = null;
                string? severity = null;
                string? analysis = null;
                string? recommendedAction = null;
                lock (entry)
                {
                    // Only sweep nodes that participate in heartbeat (LastHeartbeat set at least once)
                    // and are still stale when checked under the per-entry lock.
                    if (entry.LastHeartbeat == null) continue;
                    if (entry.LastHeartbeat >= cutoff) continue;
                    before = entry.HealthState;
                    var updated = _registry.RecordMissedInterval(nodeId);
                    if (updated.HealthState == before) continue;
                    after = updated.HealthState;
                    lastHeartbeat = entry.LastHeartbeat;
                    pipelineSegment = entry.PipelineSegment;
                    severity = updated.HealthState == "OFFLINE" ? "HIGH" : "MEDIUM";
                    analysis = $"Node {nodeId} transitioned from {before} to {updated.HealthState}. " +
                               $"Last heartbeat received at {entry.LastHeartbeat:u}.";
                    recommendedAction = updated.HealthState == "OFFLINE"
                        ? "Dispatch field technician. Node has not checked in and may be down."
                        : "Monitor node. Missed one heartbeat interval — may be intermittent.";
                }
                _logger.LogWarning(
                    "Node {NodeId} transitioned {Before} → {After} (last heartbeat: {Last:u})",
                    nodeId, before, after, lastHeartbeat);
                await _channel.Writer.WriteAsync(new TelemetryEvent
                {
                    NodeId            = nodeId,
                    PipelineSegment   = pipelineSegment,
                    EventType         = "HEALTH",
                    Severity          = severity,
                    Analysis          = analysis,
                    RecommendedAction = recommendedAction
                }, stoppingToken);
            }
        }
    }
}
