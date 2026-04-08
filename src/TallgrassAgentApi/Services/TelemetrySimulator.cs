// Services/TelemetrySimulator.cs
using System.Text.Json;
using TallgrassAgentApi.Models;

namespace TallgrassAgentApi.Services;

public class TelemetrySimulator : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TelemetryChannel _channel;
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

    public TelemetrySimulator(
        IServiceScopeFactory scopeFactory,
        TelemetryChannel channel,
        ILogger<TelemetrySimulator> logger)
    {
        _scopeFactory = scopeFactory;
        _channel = channel;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("TelemetrySimulator started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = TimeSpan.FromSeconds(_rng.Next(3, 9));
            await Task.Delay(delay, stoppingToken);

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var claude = scope.ServiceProvider.GetRequiredService<IClaudeService>();

                var pick = _rng.Next(3);
                TelemetryEvent evt = pick switch
                {
                    0 => await GenerateAlarmEventAsync(claude),
                    1 => await GenerateFlowEventAsync(claude),
                    _ => await GenerateMultiNodeEventAsync(claude)
                };

                await _channel.Writer.WriteAsync(evt, stoppingToken);
                _logger.LogDebug("Published telemetry event {EventId} ({EventType})", evt.EventId, evt.EventType);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating telemetry event.");
            }
        }

        _logger.LogInformation("TelemetrySimulator stopped.");
    }

    // ── Alarm ────────────────────────────────────────────────────────────────
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
            PipelineSegment   = Pick(Segments),
            EventType         = "ALARM",
            Severity          = response?.Severity          ?? "UNKNOWN",
            Analysis          = response?.Analysis          ?? raw,
            RecommendedAction = response?.RecommendedAction ?? string.Empty,
            CurrentValue      = request.CurrentValue,
            Threshold         = request.Threshold,
            Unit              = unit
        };
    }

    // ── Flow ─────────────────────────────────────────────────────────────────
    private async Task<TelemetryEvent> GenerateFlowEventAsync(IClaudeService _claude)
    {
        var nodeId  = Pick(NodeIds);
        var segment = Pick(Segments);
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
            VariancePercent   = response?.Variance   // FlowResponse property is named Variance
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
            AffectedNodes     = response?.AffectedNodes?.Count ?? 0   // List<string>
        };
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Deserialise the raw JSON string Claude returns.
    /// Returns null if the string is not valid JSON or doesn't match T;
    /// callers fall back to using the raw text directly.
    /// </summary>
    private static T? ParseOrDefault<T>(string raw) where T : class
    {
        try
        {
            var trimmed = raw.Trim();
            if (!trimmed.StartsWith('{')) return null;
            return JsonSerializer.Deserialize<T>(trimmed, JsonOpts);
        }
        catch
        {
            return null;
        }
    }

    private T Pick<T>(T[] arr) => arr[_rng.Next(arr.Length)];
}
