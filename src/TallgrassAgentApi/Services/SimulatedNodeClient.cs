// Services/SimulatedNodeClient.cs
using TallgrassAgentApi.Models;

namespace TallgrassAgentApi.Services;

/// <summary>
/// Development/test INodeClient. Returns realistic fake responses with
/// randomised latency. Simulates occasional degraded or offline nodes so
/// the ping endpoint exercises all code paths without real hardware.
///
/// Behavior by node ID suffix:
///   ends in 0         → always OFFLINE  (simulates a dead node)
///   ends in 5         → DEGRADED with high latency
///   anything else     → ONLINE
/// </summary>
public class SimulatedNodeClient : INodeClient
{
    private readonly Random _rng = new();
    private readonly ILogger<SimulatedNodeClient> _logger;

    public SimulatedNodeClient(ILogger<SimulatedNodeClient> logger)
    {
        _logger = logger;
    }

    public async Task<NodePingResult> PingAsync(string nodeId, CancellationToken ct = default)
    {
        _logger.LogDebug("SimulatedNodeClient pinging {NodeId}", nodeId);

        // Simulate network latency
        var latencyMs = _rng.Next(12, 180);
        await Task.Delay(latencyMs, ct);

        var lastChar = nodeId.Length > 0 ? nodeId[^1] : '1';

        if (lastChar == '0')
        {
            return new NodePingResult
            {
                NodeId       = nodeId,
                Reachable    = false,
                Status       = "OFFLINE",
                ErrorMessage = "Simulated: node not responding."
            };
        }

        if (lastChar == '5')
        {
            await Task.Delay(_rng.Next(800, 2000), ct);   // extra latency
            return new NodePingResult
            {
                NodeId          = nodeId,
                Reachable       = true,
                RoundTripMs     = latencyMs + _rng.Next(800, 2000),
                Status          = "DEGRADED",
                FirmwareVersion = "2.1.4",
                SignalStrength  = -88.0 - _rng.NextDouble() * 10,
                BatteryPercent  = 15.0 + _rng.NextDouble() * 10,
                ErrorMessage    = "Simulated: weak signal, low battery."
            };
        }

        return new NodePingResult
        {
            NodeId          = nodeId,
            Reachable       = true,
            RoundTripMs     = latencyMs,
            Status          = "ONLINE",
            FirmwareVersion = "2.4.1",
            SignalStrength  = -55.0 - _rng.NextDouble() * 20,
            BatteryPercent  = 70.0 + _rng.NextDouble() * 30
        };
    }
}
