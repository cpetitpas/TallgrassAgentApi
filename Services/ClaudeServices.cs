using System.Text;
using System.Text.Json;
using TallgrassAgentApi.Models;

namespace TallgrassAgentApi.Services;

public class ClaudeService
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

    public async Task<string> AnalyzeAlarmAsync(AlarmRequest alarm)
    {
        // Build the prompt — this is where your prompt engineering skills matter
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

        // Build the request body exactly as the Anthropic API expects it
        var requestBody = new
        {
            model = Model,
            max_tokens = 512,
            messages = new[]
            {
                new { role = "user", content = prompt }
            }
        };

        // Serialize to JSON and set required Anthropic headers
        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        _httpClient.DefaultRequestHeaders.Clear();
        _httpClient.DefaultRequestHeaders.Add("x-api-key", _apiKey);
        _httpClient.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");

        // Make the call
        var response = await _httpClient.PostAsync(ApiUrl, content);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync();
            throw new Exception($"Anthropic API error {(int)response.StatusCode}: {errorBody}");
        }

        // Parse the response — Claude's reply is nested inside content[0].text
        var responseJson = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(responseJson);
        var text = doc.RootElement
            .GetProperty("content")[0]
            .GetProperty("text")
            .GetString() ?? "";

        return text;
    }
    public async Task<string> AnalyzeFlowAsync(FlowRequest flow)
    {
        var variance = ((flow.FlowRate - flow.ExpectedFlowRate) / flow.ExpectedFlowRate) * 100;
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

        var requestBody = new
        {
            model = Model,
            max_tokens = 512,
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

        return text;
    }
}