using System.Diagnostics;
using System.Text;
using System.Text.Json;
using TallgrassAgentApi.Models;
using TallgrassAgentApi.Telemetry;

namespace TallgrassAgentApi.Services;

public class ClaudeService : IClaudeService
{
    private readonly HttpClient _httpClient;
    private readonly ClaudeThrottle _throttle;
    private readonly IAuditService _audit;
    private readonly string _apiKey;
    private const string ApiUrl = "https://api.anthropic.com/v1/messages";
    private const string Model = "claude-opus-4-6";  // Tallgrass is using this model

    // The IHttpClientFactory injects HttpClient for us (registered in Program.cs)
    public ClaudeService(IHttpClientFactory httpClientFactory, IConfiguration config, IAuditService audit, ClaudeThrottle throttle)
    {
        _httpClient = httpClientFactory.CreateClient();
        _apiKey = config["Anthropic:ApiKey"] ?? throw new Exception("Anthropic API key not configured");
        _audit = audit;
        _throttle = throttle;
    }

    private async Task<string> SendToClaudeAsync(
        string prompt,
        AuditEntryKind kind,
        string nodeId,
        CancellationToken ct,
        int maxTokens = 512)
    {
        var started = DateTimeOffset.UtcNow;
        var requestBody = new
        {
            model = Model,
            max_tokens = maxTokens,
            messages = new[]
            {
                new { role = "user", content = prompt }
            }
        };

        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, ApiUrl);
        httpRequest.Headers.Add("x-api-key", _apiKey);
        httpRequest.Headers.Add("anthropic-version", "2023-06-01");
        httpRequest.Content = content;

        using var throttleLease = await _throttle.AcquireAsync(ct);
        var response = await _httpClient.SendAsync(httpRequest, ct);
        var elapsedMs = (long)(DateTimeOffset.UtcNow - started).TotalMilliseconds;

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync();

            _audit.Record(new AuditEntry
            {
                Kind = kind,
                NodeId = nodeId,
                PromptHash = PromptHasher.Hash(prompt),
                InputTokens = 0,
                OutputTokens = 0,
                ElapsedMs = elapsedMs,
                Model = Model,
                UsedTools = false,
                StatusCode = (int)response.StatusCode
            });

            throw new Exception($"Anthropic API error {(int)response.StatusCode}: {errorBody}");
        }

        var responseJson = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(responseJson);
        var root = doc.RootElement;

        var text = "";
        if (root.TryGetProperty("content", out var contentArray) &&
            contentArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var block in contentArray.EnumerateArray())
            {
                if (block.ValueKind != JsonValueKind.Object)
                    continue;

                if (block.TryGetProperty("type", out var typeEl) &&
                    string.Equals(typeEl.GetString(), "text", StringComparison.Ordinal) &&
                    block.TryGetProperty("text", out var textEl))
                {
                    text = textEl.GetString() ?? "";
                    break;
                }
            }
        }

        // Strip markdown code fences if Claude wrapped the JSON despite instructions
        text = text.Trim();
        if (text.StartsWith("```"))
        {
            var firstNewLine = text.IndexOf('\n');
            var lastFence = text.LastIndexOf("```", StringComparison.Ordinal);
            if (firstNewLine >= 0 && lastFence > firstNewLine)
                text = text[(firstNewLine + 1)..lastFence].Trim();
        }

        var usage = root.TryGetProperty("usage", out var usageEl) ? usageEl : default;
        var inputTokens = usage.ValueKind == JsonValueKind.Object && usage.TryGetProperty("input_tokens", out var inEl)
            ? inEl.GetInt32()
            : 0;
        var outputTokens = usage.ValueKind == JsonValueKind.Object && usage.TryGetProperty("output_tokens", out var outEl)
            ? outEl.GetInt32()
            : 0;

        _audit.Record(new AuditEntry
        {
            Kind = kind,
            NodeId = nodeId,
            PromptHash = PromptHasher.Hash(prompt),
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            ElapsedMs = elapsedMs,
            Model = Model,
            UsedTools = false,
            StatusCode = (int)response.StatusCode
        });

        return text;
    }

    public async Task<string> AnalyzeAlarmAsync(AlarmRequest alarm, CancellationToken ct = default)
    {
        using var activity = TallgrassTelemetry.Claude.StartActivity("ClaudeService.AnalyzeAlarm", ActivityKind.Internal);
        activity?.SetTag("claude.model", Model);
        activity?.SetTag("tallgrass.node_id", alarm.NodeId);
        activity?.SetTag("tallgrass.alarm_type", alarm.AlarmType);
        activity?.SetTag("tallgrass.unit", alarm.Unit);

        var prompt = $"""
            You are an expert pipeline infrastructure analyst.
            Analyze the following alarm event and respond with a JSON object containing:
            - "analysis": a brief explanation of what this alarm means
            - "recommended_action": what the operator should do right now
            - "severity": one of LOW, MEDIUM, or HIGH

            Alarm Data:
            - Node ID: {alarm.NodeId}
            - Alarm Type: {alarm.AlarmType}
            - Current Value: {alarm.CurrentValue} {alarm.Unit}
            - Threshold: {alarm.Threshold} {alarm.Unit}
            - Timestamp: {alarm.Timestamp:u}

            Respond ONLY with the JSON object. No explanation, no markdown, just JSON.
            """;

        try
        {
            var result = await SendToClaudeAsync(prompt, AuditEntryKind.Alarm, alarm.NodeId, ct);
            activity?.SetTag("claude.response_length", result.Length);
            return result;
        }
        catch (Exception ex)
        {
            activity?.SetTag("error.type", ex.GetType().FullName);
            activity?.SetTag("error.message", ex.Message);
            throw;
        }
    }

    public async Task<string> AnalyzeFlowAsync(FlowRequest flow, CancellationToken ct = default)
    {
        using var activity = TallgrassTelemetry.Claude.StartActivity("ClaudeService.AnalyzeFlow", ActivityKind.Internal);
        activity?.SetTag("claude.model", Model);
        activity?.SetTag("tallgrass.node_id", flow.NodeId);
        activity?.SetTag("tallgrass.pipeline_segment", flow.PipelineSegment);
        activity?.SetTag("tallgrass.flow_direction", flow.FlowDirection);

        var variance = flow.ExpectedFlowRate != 0
            ? ((flow.FlowRate - flow.ExpectedFlowRate) / flow.ExpectedFlowRate) * 100
            : 0;
        var direction = variance >= 0 ? "above" : "below";
        activity?.SetTag("tallgrass.variance_percent", variance);

        var prompt = $"""
            You are an expert natural gas pipeline infrastructure analyst.
            Analyze the following flow rate data and respond with a JSON object containing:
            - "analysis": a brief explanation of what this flow reading indicates
            - "recommended_action": what the operator should do right now
            - "severity": one of LOW, MEDIUM, or HIGH

            Flow Data:
            - Node ID: {flow.NodeId}
            - Pipeline Segment: {flow.PipelineSegment}
            - Current Flow Rate: {flow.FlowRate} {flow.Unit}
            - Expected Flow Rate: {flow.ExpectedFlowRate} {flow.Unit}
            - Variance: {Math.Abs(variance):F1}% {direction} expected
            - Flow Direction: {flow.FlowDirection}
            - Timestamp: {flow.Timestamp:u}

            Respond ONLY with the JSON object. No explanation, no markdown, just JSON.
            """;

        try
        {
            var result = await SendToClaudeAsync(prompt, AuditEntryKind.Flow, flow.NodeId, ct);
            activity?.SetTag("claude.response_length", result.Length);
            return result;
        }
        catch (Exception ex)
        {
            activity?.SetTag("error.type", ex.GetType().FullName);
            activity?.SetTag("error.message", ex.Message);
            throw;
        }
    }

    public async Task<string> AnalyzeMultiNodeAsync(MultiNodeRequest request, CancellationToken ct = default)
    {
        using var activity = TallgrassTelemetry.Claude.StartActivity("ClaudeService.AnalyzeMultiNode", ActivityKind.Internal);
        activity?.SetTag("claude.model", Model);
        activity?.SetTag("tallgrass.region_id", request.RegionId);
        activity?.SetTag("tallgrass.readings_count", request.Readings.Count);

        var readingLines = request.Readings.Select(r =>
        {
            var variance = r.ExpectedValue != 0
                ? ((r.CurrentValue - r.ExpectedValue) / r.ExpectedValue) * 100
                : 0;
            return $"  - [{r.ReadingType}] Node {r.NodeId} | {r.MetricName}: {r.CurrentValue} {r.Unit} " +
                $"(expected {r.ExpectedValue}, variance {variance:+0.#;-0.#;0}%) | Status: {r.Status} | {r.Timestamp:u}";
        });

        var readingsBlock = string.Join("\n", readingLines);

        var prompt = $"""
        You are an expert natural gas pipeline infrastructure analyst.
        Analyze the following set of node readings across a pipeline region.

        IMPORTANT: Your response must be a single raw JSON object. 
        Do NOT wrap it in markdown. Do NOT use code fences. Do NOT add any text before or after.

        The JSON object must contain exactly these four keys:
        - "overall_status": string, one of: NORMAL, DEGRADED, or CRITICAL
        - "summary": string, a concise paragraph summarizing the region state and any patterns
        - "recommended_action": string, the single most important action an operator should take
        - "affected_nodes": array of strings, the node IDs requiring attention (use empty array if none)

        Region: {request.RegionId}
        Total Readings: {request.Readings.Count}

        Node Readings:
        {readingsBlock}

        Look for patterns across nodes — a progressive pressure drop across sequential nodes 
        suggests a leak between those nodes. Multiple flow anomalies in the same segment 
        suggest a blockage. Correlate by timestamp where relevant.

        Return raw JSON only with no additional text.
        """;

        try
        {
            var result = await SendToClaudeAsync(prompt, AuditEntryKind.MultiNode, request.RegionId, ct, maxTokens: 1024);
            activity?.SetTag("claude.response_length", result.Length);
            return result;
        }
        catch (Exception ex)
        {
            activity?.SetTag("error.type", ex.GetType().FullName);
            activity?.SetTag("error.message", ex.Message);
            throw;
        }
    }
}