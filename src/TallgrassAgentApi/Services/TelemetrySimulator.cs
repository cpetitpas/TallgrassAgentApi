// Services/TelemetrySimulator.cs
using System.Text.Json;
using TallgrassAgentApi.Models;

namespace TallgrassAgentApi.Services;

public class TelemetrySimulator : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TelemetryChannel _channel;
    private readonly NodeHealthRegistry _registry;
    private readonly ILogger<TelemetrySimulator> _logger;
    private readonly Random _rng = new();

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private static readonly string[] NodeIds =
        ["NODE-001", "NODE-002", "NODE-003", "NODE-004", "NODE-005",
         "NODE-006", "NODE-007", "NODE-008", "NODE-009", "NODE-010"];

    private static readonly string[] Segments =
        ["Segment-Alpha", "Segment-Beta", "Segment-Gamma", "Segment-Delta"];

    private static readonly string[] AlarmTypes =
        ["HIGH_PRESSURE", "LOW_PRESSURE", "HIGH_TEMPERATURE", "FLOW_ANOMALY", "COMPRESSOR_FAULT"];

    // Stable node→segment assignment so heartbeats are consistent
    private static readonly Dictionary<string, string> NodeSegments =
        NodeIds.ToDictionary(id => id, id => Segments[Math.Abs(id.GetHashCode()) % Segments.Length]);

    public TelemetrySimulator(
        IServiceScopeFactory scopeFactory,
        TelemetryChannel channel,
        NodeHealthRegistry registry,
        ILogger<TelemetrySimulator> logger)
    {
        _scopeFactory = scopeFactory;
        _channel      = channel;
        _registry     = registry;
        _logger       = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("TelemetrySimulator started.");

        // Run telemetry and heartbeat loops concurrently
        await Task.WhenAll(
            TelemetryLoopAsync(stoppingToken),
            HeartbeatLoopAsync(stoppingToken));

        _logger.LogInformation("TelemetrySimulator stopped.");
    }

    // ── Telemetry loop ────────────────────────────────────────────────────────
    private async Task TelemetryLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(_rng.Next(3, 9)), ct);

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var claude = scope.ServiceProvider.GetRequiredService<IClaudeService>();

                TelemetryEvent evt = _rng.Next(3) switch
                {
                    0 => await GenerateAlarmEventAsync(claude),
                    1 => await GenerateFlowEventAsync(claude),
                    _ => await GenerateMultiNodeEventAsync(claude)
                };

                await _channel.Writer.WriteAsync(evt, ct);
                _logger.LogDebug("Published telemetry event {EventId} ({EventType})", evt.EventId, evt.EventType);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { _logger.LogError(ex, "Error generating telemetry event."); }
        }
    }

    // ── Heartbeat loop ────────────────────────────────────────────────────────
    // Fires every 20 seconds. Each node sends a heartbeat independently on a
    // slight jitter so they don't all land at the same instant.
    private async Task HeartbeatLoopAsync(CancellationToken ct)
    {
        // Stagger startup: wait a few seconds before the first sweep so the
        // API is fully ready and the telemetry loop has already started.
        await Task.Delay(TimeSpan.FromSeconds(5), ct);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(120));

        while (await timer.WaitForNextTickAsync(ct))
        {
            foreach (var nodeId in NodeIds)
            {
                try
                {
                    // Small per-node jitter so heartbeats aren't simultaneous
                    await Task.Delay(_rng.Next(0, 1500), ct);

                    var hbRequest = new NodeHeartbeatRequest
                    {
                        NodeId          = nodeId,
                        PipelineSegment = NodeSegments[nodeId],
                        FirmwareVersion = "2.4.1",
                        SignalStrength  = -55.0 - _rng.NextDouble() * 25,
                        BatteryPercent  = 70.0  + _rng.NextDouble() * 30,
                        Timestamp       = DateTime.UtcNow
                    };

                    _registry.RecordHeartbeat(hbRequest);

                    // Publish to SSE so the dashboard receives the node state
                    await _channel.Writer.WriteAsync(new TelemetryEvent
                    {
                        NodeId            = nodeId,
                        PipelineSegment   = NodeSegments[nodeId],
                        EventType         = "HEALTH",
                        Severity          = "LOW",
                        Analysis          = $"Heartbeat received from {nodeId}.",
                        RecommendedAction = "No action required.",
                        CurrentValue      = hbRequest.SignalStrength,
                        Unit              = "dBm"
                    }, ct);

                    _logger.LogDebug("Simulated heartbeat for {NodeId}", nodeId);
                }
                catch (OperationCanceledException) { return; }
                catch (Exception ex) { _logger.LogError(ex, "Error sending simulated heartbeat for {NodeId}", nodeId); }
            }
        }
    }

    // ── Alarm ─────────────────────────────────────────────────────────────────
    private async Task<TelemetryEvent> GenerateAlarmEventAsync(IClaudeService _claude)
    {
        var nodeId    = Pick(NodeIds);
        var alarmType = Pick(AlarmTypes);
        double threshold = alarmType.Contains("PRESSURE") ? 1200.0 : alarmType.Contains("TEMP") ? 180.0 : 500.0;
        double current   = threshold * (0.6 + _rng.NextDouble() * 0.9);
        string unit      = alarmType.Contains("PRESSURE") ? "PSI" : alarmType.Contains("TEMP") ? "F" : "MCFD";

        var request = new AlarmRequest
        {
            NodeId       = nodeId,
            AlarmType    = alarmType,
            CurrentValue = Math.Round(current, 1),
            Threshold    = threshold,
            Unit         = unit,
            Timestamp    = DateTime.UtcNow
        };

        var raw      = await _claude.AnalyzeAlarmAsync(request);
        var response = ParseOrDefault<AlarmResponse>(raw);

        return new TelemetryEvent
        {
            NodeId            = nodeId,
            PipelineSegment   = NodeSegments[nodeId],
            EventType         = "ALARM",
            Severity          = response?.Severity          ?? "UNKNOWN",
            Analysis          = response?.Analysis          ?? raw,
            RecommendedAction = response?.RecommendedAction ?? string.Empty,
            CurrentValue      = request.CurrentValue,
            Threshold         = request.Threshold,
            Unit              = unit
        };
    }

    // ── Flow ──────────────────────────────────────────────────────────────────
    private async Task<TelemetryEvent> GenerateFlowEventAsync(IClaudeService _claude)
    {
        var nodeId  = Pick(NodeIds);
        var segment = NodeSegments[nodeId];
        double expected = 800.0 + _rng.NextDouble() * 400.0;
        double actual   = expected * (0.7 + _rng.NextDouble() * 0.65);

        var request = new FlowRequest
        {
            NodeId           = nodeId,
            PipelineSegment  = segment,
            FlowRate         = Math.Round(actual, 1),
            ExpectedFlowRate = Math.Round(expected, 1),
            Unit             = "MMSCFD",
            FlowDirection    = _rng.Next(2) == 0 ? "FORWARD" : "REVERSE",
            Timestamp        = DateTime.UtcNow
        };

        var raw      = await _claude.AnalyzeFlowAsync(request);
        var response = ParseOrDefault<FlowResponse>(raw);

        return new TelemetryEvent
        {
            NodeId            = nodeId,
            PipelineSegment   = segment,
            EventType         = "FLOW",
            Severity          = response?.Severity          ?? "UNKNOWN",
            Analysis          = response?.Analysis          ?? raw,
            RecommendedAction = response?.RecommendedAction ?? string.Empty,
            CurrentValue      = request.FlowRate,
            Threshold         = request.ExpectedFlowRate,
            Unit              = request.Unit,
            VariancePercent   = response?.Variance
        };
    }

    // ── Multi-node ────────────────────────────────────────────────────────────
    private async Task<TelemetryEvent> GenerateMultiNodeEventAsync(IClaudeService _claude)
    {
        var regionId = $"REGION-{(char)('A' + _rng.Next(4))}";
        int count    = _rng.Next(4, 9);

        var readings = Enumerable.Range(0, count).Select(i =>
        {
            double current  = 800 + _rng.NextDouble() * 600;
            double expected = 1000;
            return new NodeReading
            {
                NodeId        = $"NODE-{i + 1:D3}",
                ReadingType   = "ALARM",
                MetricName    = "PRESSURE",
                CurrentValue  = Math.Round(current, 1),
                ExpectedValue = expected,
                Unit          = "PSI",
                Status        = current > 1200 ? "CRITICAL" : current > 1000 ? "WARNING" : "NORMAL",
                Timestamp     = DateTime.UtcNow
            };
        }).ToList();

        var request  = new MultiNodeRequest { RegionId = regionId, Readings = readings };
        var raw      = await _claude.AnalyzeMultiNodeAsync(request);
        var response = ParseOrDefault<MultiNodeResponse>(raw);

        return new TelemetryEvent
        {
            NodeId            = regionId,
            PipelineSegment   = regionId,
            EventType         = "MULTINODE",
            Severity          = response?.OverallStatus     ?? "UNKNOWN",
            Analysis          = response?.Summary           ?? raw,
            RecommendedAction = response?.RecommendedAction ?? string.Empty,
            TotalNodes        = count,
            AffectedNodes     = response?.AffectedNodes?.Count ?? 0
        };
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    private static T? ParseOrDefault<T>(string raw) where T : class
    {
        try
        {
            var trimmed = raw.Trim();
            if (!trimmed.StartsWith('{')) return null;
            return JsonSerializer.Deserialize<T>(trimmed, JsonOpts);
        }
        catch { return null; }
    }

    private T Pick<T>(T[] arr) => arr[_rng.Next(arr.Length)];
}
