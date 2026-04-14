using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using TallgrassAgentApi.Models;

namespace TallgrassAgentApi.Services;

public class InvestigateService : IInvestigateService
{
    private readonly HttpClient      _http;
    private readonly IConfiguration  _config;
    private readonly ILogger<InvestigateService> _logger;
    private const int MaxIterations = 8;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy        = JsonNamingPolicy.CamelCase,
        WriteIndented               = false,
        DefaultIgnoreCondition      = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public InvestigateService(
        HttpClient http,
        IConfiguration config,
        ILogger<InvestigateService> logger)
    {
        _http   = http;
        _config = config;
        _logger = logger;
    }

    public async Task<InvestigateResponse> InvestigateAsync(
        InvestigateRequest request,
        CancellationToken cancellationToken = default)
    {
        var apiKey = _config["Anthropic:ApiKey"]
            ?? throw new InvalidOperationException("Anthropic:ApiKey not configured");

        // ── Seed messages ────────────────────────────────────────────────────
        var messages = new List<object>
        {
            new {
                role    = "user",
                content = $$"""
                    You are a pipeline safety agent for Tallgrass Energy.
                    Investigate the following alarm and use the available tools to gather
                    context before reaching a conclusion.

                    Node ID:      {{request.NodeId}}
                    Alarm Type:   {{request.AlarmType}}
                    Sensor Value: {{request.SensorValue}} {{request.Unit}}
                    {{(request.AdditionalContext is not null ? $"Context: {request.AdditionalContext}" : "")}}

                    Use tools to check node specs, recent telemetry, pressure thresholds,
                    maintenance history, and adjacent nodes as needed.
                    When you have enough information, respond with a JSON object only:
                    {
                      "conclusion": "...",
                      "severity": "LOW|MEDIUM|HIGH",
                      "recommended_action": "..."
                    }
                    """
            }
        };

        var toolsInvoked = new List<string>();
        var iterations   = 0;
        string conclusion        = "Unable to determine.";
        string severity          = "UNKNOWN";
        string recommendedAction = "Manual inspection required.";

        // ── Agentic loop ─────────────────────────────────────────────────────
        while (iterations < MaxIterations)
        {
            iterations++;

            var requestBody = new
            {
                model      = "claude-opus-4-6",
                max_tokens = 1024,
                tools      = PipelineTools.Definitions,
                messages
            };

            var json    = JsonSerializer.Serialize(requestBody, JsonOpts);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post,
                "https://api.anthropic.com/v1/messages");
            httpRequest.Headers.Add("x-api-key", apiKey);
            httpRequest.Headers.Add("anthropic-version", "2023-06-01");
            httpRequest.Content = content;

            using var httpResponse = await _http.SendAsync(httpRequest, cancellationToken);
            var responseJson = await httpResponse.Content.ReadAsStringAsync(cancellationToken);

            if (!httpResponse.IsSuccessStatusCode)
            {
                _logger.LogError("Anthropic API error {Status}: {Body}",
                    (int)httpResponse.StatusCode, responseJson);
                break;
            }

            using var doc   = JsonDocument.Parse(responseJson);
            var root        = doc.RootElement;
            var stopReason  = root.GetProperty("stop_reason").GetString();
            var contentArr  = root.GetProperty("content");

            // Collect all blocks in this turn
            var assistantBlocks = new List<object>();
            var toolUseBlocks   = new List<ToolInvocation>();

            foreach (var block in contentArr.EnumerateArray())
            {
                var type = block.GetProperty("type").GetString();

                if (type == "text")
                {
                    var text = block.GetProperty("text").GetString() ?? "";
                    assistantBlocks.Add(new { type = "text", text });

                    // Try to parse the final JSON answer
                    var trimmed = text.Trim();
                    if (trimmed.StartsWith('{'))
                    {
                        try
                        {
                            using var answerDoc = JsonDocument.Parse(trimmed);
                            var ans = answerDoc.RootElement;
                            conclusion        = ans.TryGetProperty("conclusion",          out var c) ? c.GetString() ?? conclusion        : conclusion;
                            severity          = ans.TryGetProperty("severity",            out var s) ? s.GetString() ?? severity          : severity;
                            recommendedAction = ans.TryGetProperty("recommended_action",  out var r) ? r.GetString() ?? recommendedAction : recommendedAction;
                        }
                        catch { /* not JSON — ignore */ }
                    }
                }
                else if (type == "tool_use")
                {
                    var toolUseId = block.GetProperty("id").GetString()    ?? "";
                    var toolName  = block.GetProperty("name").GetString()   ?? "";
                    var toolInput = block.GetProperty("input");

                    assistantBlocks.Add(new
                    {
                        type  = "tool_use",
                        id    = toolUseId,
                        name  = toolName,
                        input = toolInput
                    });

                    toolUseBlocks.Add(new ToolInvocation
                    {
                        ToolUseId = toolUseId,
                        ToolName  = toolName,
                        Input     = toolInput.Clone()
                    });
                }
            }

            // Append assistant turn to history
            messages.Add(new { role = "assistant", content = assistantBlocks });

            if (stopReason == "end_turn" || !toolUseBlocks.Any())
                break;

            // Execute each tool and append a user turn with all results
            var toolResults = new List<object>();
            foreach (var invocation in toolUseBlocks)
            {
                toolsInvoked.Add(invocation.ToolName);
                _logger.LogDebug("Tool call: {Tool} input={Input}",
                    invocation.ToolName,
                    invocation.Input.GetRawText());

                var result = PipelineTools.Execute(invocation.ToolName, invocation.Input);

                toolResults.Add(new
                {
                    type        = "tool_result",
                    tool_use_id = invocation.ToolUseId,
                    content     = result
                });
            }

            messages.Add(new { role = "user", content = toolResults });
        }

        return new InvestigateResponse
        {
            NodeId            = request.NodeId,
            Conclusion        = conclusion,
            Severity          = severity,
            RecommendedAction = recommendedAction,
            ToolsInvoked      = toolsInvoked,
            Iterations        = iterations
        };
    }
}