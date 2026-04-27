using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using TallgrassAgentApi.Models;
using TallgrassAgentApi.Telemetry;

namespace TallgrassAgentApi.Services;

public class MultiNodeInvestigateService : IMultiNodeInvestigateService
{
    private readonly IInvestigateService            _investigateSvc;
    private readonly HttpClient                     _http;
    private readonly IConfiguration                 _config;
    private readonly ClaudeThrottle                 _throttle;
    private readonly IAuditService                  _audit;
    private readonly ILogger<MultiNodeInvestigateService> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy   = JsonNamingPolicy.CamelCase,
        WriteIndented          = false,
        DefaultIgnoreCondition =
            System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public MultiNodeInvestigateService(
        IInvestigateService investigateSvc,
        HttpClient http,
        IConfiguration config,
        ClaudeThrottle throttle,
        IAuditService audit,
        ILogger<MultiNodeInvestigateService> logger)
    {
        _investigateSvc = investigateSvc;
        _http           = http;
        _config         = config;
        _throttle       = throttle;
        _audit          = audit;
        _logger         = logger;
    }

    public async Task<MultiNodeInvestigateResponse> InvestigateAsync(
        MultiNodeInvestigateRequest request,
        CancellationToken cancellationToken = default)
    {
        // ── Phase 1: bounded-parallel per-node investigations ─────────────
        // Run at most NodeParallelism investigations concurrently so that the
        // shared ClaudeThrottle is never stampeded by all nodes at once.
        int nodeParallelism = _config.GetValue<int>("ClaudeThrottle:NodeParallelism",
            _config.GetValue<int>("ClaudeThrottle:MaxConcurrent", 3));

        if (nodeParallelism <= 0)
        {
            throw new InvalidOperationException(
                $"Configuration value 'ClaudeThrottle:NodeParallelism' must be greater than 0, but was {nodeParallelism}.");
        }

        var rawResultsBag = new System.Collections.Concurrent.ConcurrentBag<NodeInvestigationResult>();

        try
        {
            await Parallel.ForEachAsync(
                request.Nodes,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = nodeParallelism,
                    CancellationToken      = cancellationToken
                },
                async (node, ct) =>
                {
                    var r = await _investigateSvc.InvestigateAsync(
                        new InvestigateRequest
                        {
                            NodeId            = node.NodeId,
                            AlarmType         = node.AlarmType,
                            SensorValue       = node.SensorValue,
                            Unit              = node.Unit,
                            AdditionalContext = request.RegionContext
                        }, ct);

                    rawResultsBag.Add(new NodeInvestigationResult
                    {
                        NodeId            = r.NodeId,
                        Conclusion        = r.Conclusion,
                        Severity          = r.Severity,
                        RecommendedAction = r.RecommendedAction,
                        ToolsInvoked      = r.ToolsInvoked,
                        Iterations        = r.Iterations
                    });
                });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "One or more per-node investigations failed.");
            throw;
        }

        // Preserve original request order for deterministic output
        var nodeOrder = request.Nodes.Select((n, i) => (n.NodeId, i))
                                     .ToDictionary(x => x.NodeId, x => x.i);
        NodeInvestigationResult[] rawResults = rawResultsBag
            .OrderBy(r => nodeOrder.TryGetValue(r.NodeId, out var idx) ? idx : int.MaxValue)
            .ToArray();

        // ── Phase 2: synthesis call ───────────────────────────────────────
        var synthesis = await SynthesizeAsync(request, rawResults, cancellationToken);

        return new MultiNodeInvestigateResponse
        {
            RootCauseHypothesis = synthesis.rootCause,
            OverallSeverity     = synthesis.severity,
            RecommendedAction   = synthesis.action,
            CorrelationSummary  = synthesis.correlation,
            NodeResults         = rawResults.ToList(),
            TotalIterations     = rawResults.Sum(r => r.Iterations),
            AffectedNodes       = rawResults
                                    .Where(r =>
                                        string.Equals(r.Severity, "HIGH", StringComparison.OrdinalIgnoreCase) ||
                                        string.Equals(r.Severity, "MEDIUM", StringComparison.OrdinalIgnoreCase))
                                    .Select(r => r.NodeId)
                                    .ToList()
        };
    }

    private async Task<(string rootCause, string severity, string action, string correlation)>
        SynthesizeAsync(
            MultiNodeInvestigateRequest request,
            NodeInvestigationResult[]   results,
            CancellationToken           cancellationToken)
    {
        using var activity = TallgrassTelemetry.Investigate.StartActivity(
            "MultiNodeInvestigateService.Synthesize",
            ActivityKind.Internal);
        activity?.SetTag("claude.model", "claude-opus-4-6");
        activity?.SetTag("tallgrass.region_context", request.RegionContext ?? string.Empty);
        activity?.SetTag("multinode.node_count", results.Length);

        var apiKey = _config["Anthropic:ApiKey"]
            ?? throw new InvalidOperationException("Anthropic:ApiKey not configured");

        // Build a structured summary of all per-node findings for the prompt
        var findings = string.Join("\n\n", results.Select(r =>
            $"Node: {r.NodeId}\n" +
            $"Severity: {r.Severity}\n" +
            $"Conclusion: {r.Conclusion}\n" +
            $"Recommended Action: {r.RecommendedAction}\n" +
            $"Tools Used: {string.Join(", ", r.ToolsInvoked)}"));

        var regionLine = request.RegionContext is not null
            ? $"in region: {request.RegionContext}."
            : ".";

        var prompt = new StringBuilder()
            .AppendLine("You are a senior pipeline safety analyst for Tallgrass Energy.")
            .AppendLine($"You have received individual investigation reports for {results.Length} nodes")
            .AppendLine(regionLine)
            .AppendLine()
            .AppendLine("Individual findings:")
            .AppendLine(findings)
            .AppendLine()
            .AppendLine("Analyze these findings together and respond ONLY with a JSON object:")
            .AppendLine("{")
            .AppendLine("  \"root_cause_hypothesis\": \"...\",")
            .AppendLine("  \"overall_severity\": \"LOW|MEDIUM|HIGH\",")
            .AppendLine("  \"recommended_action\": \"...\",")
            .AppendLine("  \"correlation_summary\": \"...\"")
            .AppendLine("}")
            .AppendLine()
            .AppendLine("root_cause_hypothesis: the most likely single cause explaining all findings.")
            .AppendLine("overall_severity: the highest justified severity across all nodes.")
            .AppendLine("recommended_action: the single most important action an operator should take now.")
            .Append("correlation_summary: 2-3 sentences on how the node findings relate to each other.")
            .ToString();

        var requestBody = new
        {
            model      = "claude-opus-4-6",
            max_tokens = 1024,
            messages   = new[] { new { role = "user", content = prompt } }
        };

        var json        = JsonSerializer.Serialize(requestBody, JsonOpts);
        var httpContent = new StringContent(json, Encoding.UTF8, "application/json");

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post,
            "https://api.anthropic.com/v1/messages");
        httpRequest.Headers.Add("x-api-key", apiKey);
        httpRequest.Headers.Add("anthropic-version", "2023-06-01");
        httpRequest.Content = httpContent;

        var sw = System.Diagnostics.Stopwatch.StartNew();
        using var slot = await _throttle.AcquireAsync(cancellationToken);
        using var httpResponse = await _http.SendAsync(httpRequest, cancellationToken);
        sw.Stop();
        activity?.SetTag("http.status_code", (int)httpResponse.StatusCode);
        activity?.SetTag("multinode.synthesis_elapsed_ms", sw.ElapsedMilliseconds);

        var responseJson = await httpResponse.Content.ReadAsStringAsync(cancellationToken);

        // Audit
        int inputTokens = 0, outputTokens = 0;
        try
        {
            using var d = JsonDocument.Parse(responseJson);
            if (d.RootElement.TryGetProperty("usage", out var usage))
            {
                inputTokens  = usage.TryGetProperty("input_tokens",  out var i) ? i.GetInt32() : 0;
                outputTokens = usage.TryGetProperty("output_tokens", out var o) ? o.GetInt32() : 0;
            }
        }
        catch { }

        _audit.Record(new AuditEntry
        {
            Kind         = AuditEntryKind.MultiNode,
            NodeId       = string.Join(",", results.Select(r => r.NodeId)),
            PromptHash   = PromptHasher.Hash(prompt),
            InputTokens  = inputTokens,
            OutputTokens = outputTokens,
            ElapsedMs    = sw.ElapsedMilliseconds,
            Model        = "claude-opus-4-6",
            StatusCode   = (int)httpResponse.StatusCode
        });
        activity?.SetTag("claude.input_tokens", inputTokens);
        activity?.SetTag("claude.output_tokens", outputTokens);

        if (!httpResponse.IsSuccessStatusCode)
        {
            activity?.SetTag("error.type", "HttpRequestException");
            activity?.SetTag("error.message", $"Anthropic API returned {(int)httpResponse.StatusCode}");
            _logger.LogError("Synthesis API error {Status}: {Body}",
                (int)httpResponse.StatusCode, responseJson);
            throw new HttpRequestException(
                $"Anthropic API returned {(int)httpResponse.StatusCode}");
        }

        // Parse synthesis response
        try
        {
            using var doc = JsonDocument.Parse(responseJson);
            var text = doc.RootElement
                .GetProperty("content")[0]
                .GetProperty("text")
                .GetString() ?? "";

            var trimmed = text.Trim();
            // Strip markdown code fences if Claude wrapped the JSON despite instructions
            if (trimmed.StartsWith("```"))
            {
                var firstNewLine = trimmed.IndexOf('\n');
                var lastFence    = trimmed.LastIndexOf("```", StringComparison.Ordinal);
                if (firstNewLine >= 0 && lastFence > firstNewLine)
                    trimmed = trimmed[(firstNewLine + 1)..lastFence].Trim();
            }
            using var ans = JsonDocument.Parse(trimmed);
            var root = ans.RootElement;

            activity?.SetTag("multinode.overall_severity",
                root.TryGetProperty("overall_severity", out var sTag) ? sTag.GetString() ?? "UNKNOWN" : "UNKNOWN");

            return (
                rootCause:   root.TryGetProperty("root_cause_hypothesis", out var rc) ? rc.GetString() ?? "" : "",
                severity:    root.TryGetProperty("overall_severity",       out var sv) ? sv.GetString() ?? "UNKNOWN" : "UNKNOWN",
                action:      root.TryGetProperty("recommended_action",     out var ra) ? ra.GetString() ?? "" : "",
                correlation: root.TryGetProperty("correlation_summary",    out var cs) ? cs.GetString() ?? "" : ""
            );
        }
        catch (Exception ex)
        {
            activity?.SetTag("error.type", ex.GetType().FullName);
            activity?.SetTag("error.message", ex.Message);
            _logger.LogError(ex, "Failed to parse synthesis response: {Body}", responseJson);
            return ("Unable to synthesize findings.", "UNKNOWN", "Manual review required.", "");
        }
    }
}