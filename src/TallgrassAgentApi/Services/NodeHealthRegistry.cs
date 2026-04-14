// Services/NodeHealthRegistry.cs
using System.Collections.Concurrent;
using TallgrassAgentApi.Models;

namespace TallgrassAgentApi.Services;

/// <summary>
/// Singleton in-memory registry of per-node health state.
/// Updated by:
///   - NodeController  when a heartbeat arrives (push)
///   - NodeController  when a ping completes    (pull)
///   - NodeHealthSweep when a node goes stale   (background)
/// Read by:
///   - NodeController  GET /api/nodes and GET /api/nodes/{nodeId}
///   - NodeHealthSweep to detect missed intervals
/// </summary>
public class NodeHealthRegistry
{
    private readonly ConcurrentDictionary<string, NodeHealthEntry> _entries = new();

    public IReadOnlyDictionary<string, NodeHealthEntry> All => _entries;

    /// <summary>Called when a heartbeat POST arrives from a node.</summary>
    public NodeHealthEntry RecordHeartbeat(NodeHeartbeatRequest request)
    {
        var entry = _entries.GetOrAdd(request.NodeId, id => new NodeHealthEntry { NodeId = id });

        lock (entry)
        {
            entry.PipelineSegment = request.PipelineSegment;
            entry.LastHeartbeat   = request.Timestamp;
            entry.MissedIntervals = 0;
            entry.HealthState     = "HEALTHY";
            entry.FirmwareVersion = request.FirmwareVersion;
            entry.SignalStrength  = request.SignalStrength;
            entry.BatteryPercent  = request.BatteryPercent;
        }

        return entry;
    }

    /// <summary>Called when a ping completes.</summary>
    public NodeHealthEntry RecordPing(NodePingResult result)
    {
        var entry = _entries.GetOrAdd(result.NodeId, id => new NodeHealthEntry { NodeId = id });

        lock (entry)
        {
            entry.LastPing       = result.Timestamp;
            entry.LastRoundTripMs = result.RoundTripMs;
            entry.FirmwareVersion = result.FirmwareVersion ?? entry.FirmwareVersion;
            entry.SignalStrength  = result.SignalStrength  ?? entry.SignalStrength;
            entry.BatteryPercent  = result.BatteryPercent  ?? entry.BatteryPercent;

            // Only update health state if ping result is worse than current heartbeat state
            entry.HealthState = result.Status switch
            {
                "OFFLINE"  => "OFFLINE",
                "DEGRADED" => entry.HealthState == "OFFLINE" ? "OFFLINE" : "DEGRADED",
                _          => entry.HealthState == "OFFLINE" || entry.HealthState == "DEGRADED"
                                  ? entry.HealthState   // heartbeat drives recovery, not ping
                                  : "HEALTHY"
            };
        }

        return entry;
    }

    /// <summary>Called by NodeHealthSweep for each node that missed a heartbeat interval.</summary>
    public NodeHealthEntry RecordMissedInterval(string nodeId)
    {
        var entry = _entries.GetOrAdd(nodeId, id => new NodeHealthEntry { NodeId = id });

        lock (entry)
        {
            entry.MissedIntervals++;
            entry.HealthState = entry.MissedIntervals switch
            {
                1 => "DEGRADED",
                _ => "OFFLINE"
            };
        }

        return entry;
    }

    public NodeHealthEntry? Get(string nodeId) =>
        _entries.TryGetValue(nodeId, out var entry) ? entry : null;
}
