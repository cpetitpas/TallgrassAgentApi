using System.Text;
using System.Text.Json;
using TallgrassAgentApi.Models;

namespace TallgrassAgentApi.Services;

public class ClaudeService : IClaudeService
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private const string ApiUrl = "https://api.anthropic.com/v1/messages";
    private const string Model = "claude-opus-4-6";  // Tallgrass is using this model

    // The IHttpClientFactory injects HttpClient for us (registered in Program.cs)
    public ClaudeService(IHttpClientFactory httpClientFactory, IConfiguration config)
    {
        _httpClient = httpClientFactory.CreateClient();
        _apiKey = config["Anthropic:ApiKey"] ?? throw new Exception("Anthropic API key not configured");
    }

    private async Task<string> SendToClaudeAsync(string prompt, int maxTokens = 512)
    {
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

        _httpClient.DefaultRequestHeaders.Clear();
        _httpClient.DefaultRequestHeaders.Add("x-api-key", _apiKey);
        _httpClient.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");

        var response = await _httpClient.PostAsync(ApiUrl, content);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync();
            throw new Exception($"Anthropic API error {(int)response.StatusCode}: {errorBody}");
        }

        var responseJson = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(responseJson);
        var text = doc.RootElement
            .GetProperty("content")[0]
            .GetProperty("text")
            .GetString() ?? "";

        // Strip markdown code fences if Claude wrapped the JSON despite instructions
        text = text.Trim();
        if (text.StartsWith("```"))
        {
            text = text.Substring(text.IndexOf('\n') + 1);
            text = text.Substring(0, text.LastIndexOf("```")).Trim();
        }

        return text;
    }

    public async Task<string> AnalyzeAlarmAsync(AlarmRequest alarm)
    {
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

        return await SendToClaudeAsync(prompt);
    }

    public async Task<string> AnalyzeFlowAsync(FlowRequest flow)
    {
        var variance = flow.ExpectedFlowRate != 0
            ? ((flow.FlowRate - flow.ExpectedFlowRate) / flow.ExpectedFlowRate) * 100
            : 0;
        var direction = variance >= 0 ? "above" : "below";

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

        return await SendToClaudeAsync(prompt);
    }

    public async Task<string> AnalyzeMultiNodeAsync(MultiNodeRequest request)
    {
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

        return await SendToClaudeAsync(prompt, maxTokens: 1024);
    }
}