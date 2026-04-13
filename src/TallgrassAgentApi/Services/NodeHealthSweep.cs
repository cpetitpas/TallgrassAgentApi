// Services/NodeHealthSweep.cs
using TallgrassAgentApi.Models;

namespace TallgrassAgentApi.Services;

/// <summary>
/// Background service that runs every HeartbeatIntervalSeconds and checks
/// each registered node for a missed heartbeat. Nodes that miss intervals
/// are transitioned HEALTHY → DEGRADED → OFFLINE and a TelemetryEvent is
/// published to the SSE channel so the dashboard reflects the state change.
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
                // Only sweep nodes that participate in heartbeat (LastHeartbeat set at least once)
                if (entry.LastHeartbeat == null) continue;
                if (entry.LastHeartbeat >= cutoff) continue;

                var before = entry.HealthState;
                var updated = _registry.RecordMissedInterval(nodeId);

                if (updated.HealthState == before) continue;

                _logger.LogWarning(
                    "Node {NodeId} transitioned {Before} → {After} (last heartbeat: {Last:u})",
                    nodeId, before, updated.HealthState, entry.LastHeartbeat);

                await _channel.Writer.WriteAsync(new TelemetryEvent
                {
                    NodeId            = nodeId,
                    PipelineSegment   = entry.PipelineSegment,
                    EventType         = "HEALTH",
                    Severity          = updated.HealthState == "OFFLINE" ? "HIGH" : "MEDIUM",
                    Analysis          = $"Node {nodeId} transitioned from {before} to {updated.HealthState}. " +
                                        $"Last heartbeat received at {entry.LastHeartbeat:u}.",
                    RecommendedAction = updated.HealthState == "OFFLINE"
                        ? "Dispatch field technician. Node has not checked in and may be down."
                        : "Monitor node. Missed one heartbeat interval — may be intermittent."
                }, stoppingToken);
            }
        }
    }
}
