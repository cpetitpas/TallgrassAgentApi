using System.Text;
using System.Text.Json;
using TallgrassAgentApi.Models;

namespace TallgrassAgentApi.Services;

public class InvestigateService : IInvestigateService
{
    private readonly HttpClient      _http;
    private readonly IAuditService   _audit;
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
        IAuditService audit,
        IConfiguration config,
        ILogger<InvestigateService> logger)
    {
        _http   = http;
        _audit  = audit;
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

            var started = DateTimeOffset.UtcNow;
            using var httpResponse = await _http.SendAsync(httpRequest, cancellationToken);
            var responseJson = await httpResponse.Content.ReadAsStringAsync(cancellationToken);
            var elapsedMs = (long)(DateTimeOffset.UtcNow - started).TotalMilliseconds;

            if (!httpResponse.IsSuccessStatusCode)
            {
                _audit.Record(new AuditEntry
                {
                    Kind = AuditEntryKind.Investigate,
                    NodeId = request.NodeId,
                    PromptHash = PromptHasher.Hash(json),
                    InputTokens = 0,
                    OutputTokens = 0,
                    ElapsedMs = elapsedMs,
                    Model = "claude-opus-4-6",
                    UsedTools = false,
                    StatusCode = (int)httpResponse.StatusCode
                });

                _logger.LogError("Anthropic API error {Status}: {Body}",
                    (int)httpResponse.StatusCode, responseJson);
                throw new HttpRequestException(
                    $"Anthropic API returned {(int)httpResponse.StatusCode}: {responseJson}",
                    null,
                    httpResponse.StatusCode);
            }

            using var doc   = JsonDocument.Parse(responseJson);
            var root        = doc.RootElement;
            var stopReason  = root.GetProperty("stop_reason").GetString();
            var contentArr  = root.GetProperty("content");
            var usage       = root.TryGetProperty("usage", out var usageEl) ? usageEl : default;
            var inputTokens = usage.ValueKind == JsonValueKind.Object && usage.TryGetProperty("input_tokens", out var inEl)
                ? inEl.GetInt32()
                : 0;
            var outputTokens = usage.ValueKind == JsonValueKind.Object && usage.TryGetProperty("output_tokens", out var outEl)
                ? outEl.GetInt32()
                : 0;
            var usedTools = contentArr.EnumerateArray()
                .Any(block => block.TryGetProperty("type", out var t) && t.GetString() == "tool_use");

            _audit.Record(new AuditEntry
            {
                Kind = AuditEntryKind.Investigate,
                NodeId = request.NodeId,
                PromptHash = PromptHasher.Hash(json),
                InputTokens = inputTokens,
                OutputTokens = outputTokens,
                ElapsedMs = elapsedMs,
                Model = "claude-opus-4-6",
                UsedTools = usedTools,
                StatusCode = (int)httpResponse.StatusCode
            });

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

                    // Try to parse the final JSON answer from raw, fenced, or mixed text.
                    if (TryParseConclusionPayload(text, out var parsedConclusion, out var parsedSeverity, out var parsedAction))
                    {
                        conclusion        = parsedConclusion ?? conclusion;
                        severity          = parsedSeverity ?? severity;
                        recommendedAction = parsedAction ?? recommendedAction;
                    }
                }
                else if (type == "tool_use")
                {
                    var toolUseId = block.GetProperty("id").GetString()    ?? "";
                    var toolName  = block.GetProperty("name").GetString()   ?? "";
                    var toolInput = block.GetProperty("input").Clone();

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
                        Input     = toolInput
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

    private static bool TryParseConclusionPayload(
        string text,
        out string? conclusion,
        out string? severity,
        out string? recommendedAction)
    {
        conclusion = null;
        severity = null;
        recommendedAction = null;

        var candidates = BuildJsonCandidates(text);
        foreach (var candidate in candidates)
        {
            try
            {
                using var answerDoc = JsonDocument.Parse(candidate);
                var ans = answerDoc.RootElement;

                conclusion = ans.TryGetProperty("conclusion", out var c) ? c.GetString() : null;
                severity = ans.TryGetProperty("severity", out var s) ? s.GetString() : null;
                recommendedAction = ans.TryGetProperty("recommended_action", out var r) ? r.GetString() : null;

                if (!string.IsNullOrWhiteSpace(conclusion) ||
                    !string.IsNullOrWhiteSpace(severity) ||
                    !string.IsNullOrWhiteSpace(recommendedAction))
                {
                    return true;
                }
            }
            catch
            {
                // Ignore malformed candidates and keep trying.
            }
        }

        return false;
    }

    private static IEnumerable<string> BuildJsonCandidates(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.StartsWith('{') && trimmed.EndsWith('}'))
        {
            yield return trimmed;
        }

        var fenceStart = text.IndexOf("```");
        if (fenceStart >= 0)
        {
            var contentStart = text.IndexOf('\n', fenceStart);
            if (contentStart >= 0)
            {
                var fenceEnd = text.IndexOf("```", contentStart + 1, StringComparison.Ordinal);
                if (fenceEnd > contentStart)
                {
                    var fenced = text[(contentStart + 1)..fenceEnd].Trim();
                    if (!string.IsNullOrWhiteSpace(fenced))
                    {
                        yield return fenced;
                    }
                }
            }
        }

        var firstBrace = text.IndexOf('{');
        var lastBrace = text.LastIndexOf('}');
        if (firstBrace >= 0 && lastBrace > firstBrace)
        {
            var span = text[firstBrace..(lastBrace + 1)].Trim();
            if (!string.IsNullOrWhiteSpace(span))
            {
                yield return span;
            }
        }
    }
}