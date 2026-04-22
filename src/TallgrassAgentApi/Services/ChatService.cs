using System.Text;
using System.Text.Json;
using TallgrassAgentApi.Models;

namespace TallgrassAgentApi.Services;

public class ChatService : IChatService
{
    private readonly HttpClient                 _http;
    private readonly ClaudeThrottle             _throttle;
    private readonly IAuditService              _audit;
    private readonly IConfiguration             _config;
    private readonly IConversationStore         _store;
    private readonly ILogger<ChatService>       _logger;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy   = JsonNamingPolicy.CamelCase,
        WriteIndented          = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private const string SystemPrompt = """
        You are a pipeline operations assistant for Tallgrass Energy.
        You help operators understand and respond to pipeline alarms and anomalies.
        Be concise and technical. When referencing sensor values use the correct units.
        If asked why a severity was assigned, explain your reasoning based on the data you were given.
        If you do not have enough information to answer a question, say so clearly.
        Do not invent sensor readings or maintenance records.
        """;

    public ChatService(
        HttpClient           http,
        ClaudeThrottle       throttle,
        IAuditService        audit,
        IConfiguration       config,
        IConversationStore   store,
        ILogger<ChatService> logger)
    {
        _http   = http;
        _throttle = throttle;
        _audit  = audit;
        _config = config;
        _store  = store;
        _logger = logger;
    }

    public async Task<ChatResponse> SendAsync(
        string      incidentId,
        string      nodeId,
        ChatRequest request,
        CancellationToken cancellationToken = default)
    {
        var apiKey = _config["Anthropic:ApiKey"]
            ?? throw new InvalidOperationException("Anthropic:ApiKey not configured");

        // Ensure conversation exists
        var state = _store.GetOrCreate(incidentId, nodeId);

        // Append the incoming user message
        var userMessage = new ChatMessage
        {
            Role      = "user",
            Content   = request.Message,
            Timestamp = DateTimeOffset.UtcNow
        };
        _store.Append(incidentId, userMessage);

        // Build messages array for Anthropic — full history every time
        List<ChatMessage> snapshot;
        lock (state.Messages)
            snapshot = state.Messages.ToList();

        var apiMessages = snapshot.Select(m => new { role = m.Role, content = m.Content }).ToList();

        var requestBody = new
        {
            model      = "claude-opus-4-6",
            max_tokens = 1024,
            system     = SystemPrompt,
            messages   = apiMessages
        };

        var json        = JsonSerializer.Serialize(requestBody, JsonOpts);
        var httpContent = new StringContent(json, Encoding.UTF8, "application/json");

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post,
            "https://api.anthropic.com/v1/messages");
        httpRequest.Headers.Add("x-api-key", apiKey);
        httpRequest.Headers.Add("anthropic-version", "2023-06-01");
        httpRequest.Content = httpContent;

        using var throttleLease = await _throttle.AcquireAsync(cancellationToken);
        var started = DateTimeOffset.UtcNow;
        using var httpResponse = await _http.SendAsync(httpRequest, cancellationToken);
        var responseJson = await httpResponse.Content.ReadAsStringAsync(cancellationToken);
        var elapsedMs = (long)(DateTimeOffset.UtcNow - started).TotalMilliseconds;

        if (!httpResponse.IsSuccessStatusCode)
        {
            _audit.Record(new AuditEntry
            {
                Kind = AuditEntryKind.Chat,
                NodeId = nodeId,
                IncidentId = incidentId,
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
                $"Anthropic API returned {(int)httpResponse.StatusCode}");
        }

        using var doc = JsonDocument.Parse(responseJson);
        var replyBuilder = new StringBuilder();
        if (doc.RootElement.TryGetProperty("content", out var contentElement) &&
            contentElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var contentBlock in contentElement.EnumerateArray())
            {
                if (contentBlock.ValueKind != JsonValueKind.Object)
                    continue;
                if (!contentBlock.TryGetProperty("type", out var typeElement) ||
                    !string.Equals(typeElement.GetString(), "text", StringComparison.Ordinal))
                    continue;
                if (contentBlock.TryGetProperty("text", out var textElement))
                {
                    var text = textElement.GetString();
                    if (!string.IsNullOrEmpty(text))
                        replyBuilder.Append(text);
                }
            }
        }
        var replyText = replyBuilder.ToString();

        var usage = doc.RootElement.TryGetProperty("usage", out var usageEl) ? usageEl : default;
        var inputTokens = usage.ValueKind == JsonValueKind.Object && usage.TryGetProperty("input_tokens", out var inEl)
            ? inEl.GetInt32()
            : 0;
        var outputTokens = usage.ValueKind == JsonValueKind.Object && usage.TryGetProperty("output_tokens", out var outEl)
            ? outEl.GetInt32()
            : 0;

        _audit.Record(new AuditEntry
        {
            Kind = AuditEntryKind.Chat,
            NodeId = nodeId,
            IncidentId = incidentId,
            PromptHash = PromptHasher.Hash(json),
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            ElapsedMs = elapsedMs,
            Model = "claude-opus-4-6",
            UsedTools = false,
            StatusCode = (int)httpResponse.StatusCode
        });

        // Append assistant reply
        var assistantMessage = new ChatMessage
        {
            Role      = "assistant",
            Content   = replyText,
            Timestamp = DateTimeOffset.UtcNow
        };
        _store.Append(incidentId, assistantMessage);

        List<ChatMessage> finalSnapshot;
        lock (state.Messages)
            finalSnapshot = state.Messages.ToList();

        return new ChatResponse
        {
            IncidentId = incidentId,
            Reply      = replyText,
            TurnCount  = finalSnapshot.Count(m => m.Role == "user"),
            History    = finalSnapshot
        };
    }
}