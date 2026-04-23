using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using TallgrassAgentApi.Controllers;
using TallgrassAgentApi.Models;
using TallgrassAgentApi.Services;
using Xunit;

namespace TallgrassAgentApi.Tests;

// ── Fake HTTP handler ─────────────────────────────────────────────────────────

public class FakeChatHttpHandler : HttpMessageHandler
{
    public int CallCount { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        CallCount++;
        var body = $$"""
            {
              "content": [{ "type": "text", "text": "Reply #{{CallCount}}: pressure spike is within operational limits." }],
              "stop_reason": "end_turn"
            }
            """;
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        });
    }
}

// ── Tests ─────────────────────────────────────────────────────────────────────

public class IncidentChatTests
{
    private static (ChatService svc, IConversationStore store, AuditService audit) BuildWithAudit(HttpMessageHandler handler)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Anthropic:ApiKey"] = "test" })
            .Build();
        var store = new InMemoryConversationStore();
        var audit = new AuditService();
        var throttle = new ClaudeThrottle(config);
        var svc   = new ChatService(new HttpClient(handler), throttle, audit, config, store,
                        NullLogger<ChatService>.Instance);
        return (svc, store, audit);
    }

    private static (ChatService svc, IConversationStore store) Build(HttpMessageHandler handler)
    {
        var (svc, store, _) = BuildWithAudit(handler);
        return (svc, store);
    }

    [Fact]
    public async Task FirstTurn_CreatesConversationAndReturnsReply()
    {
        var (svc, store) = Build(new FakeChatHttpHandler());

        var response = await svc.SendAsync("INC-001", "NODE-003",
            new ChatRequest { Message = "Why was this flagged HIGH?" });

        Assert.Equal("INC-001", response.IncidentId);
        Assert.NotEmpty(response.Reply);
        Assert.Equal(1, response.TurnCount);

        var state = store.Get("INC-001");
        Assert.NotNull(state);
        Assert.Equal(2, state!.Messages.Count); // user + assistant
    }

    [Fact]
    public async Task MultipleTurns_HistoryGrows()
    {
        var handler      = new FakeChatHttpHandler();
        var (svc, store) = Build(handler);

        await svc.SendAsync("INC-002", "NODE-003", new ChatRequest { Message = "What happened?" });
        await svc.SendAsync("INC-002", "NODE-003", new ChatRequest { Message = "What should I do?" });
        await svc.SendAsync("INC-002", "NODE-003", new ChatRequest { Message = "Is NODE-002 affected?" });

        Assert.Equal(3, handler.CallCount);
        var state = store.Get("INC-002");
        Assert.Equal(6, state!.Messages.Count); // 3 user + 3 assistant
    }

    [Fact]
    public async Task Audit_RecordsOneEntryPerTurn()
    {
        var handler = new FakeChatHttpHandler();
        var (svc, _, audit) = BuildWithAudit(handler);

        await svc.SendAsync("INC-AUDIT", "NODE-003", new ChatRequest { Message = "Turn one" });
        await svc.SendAsync("INC-AUDIT", "NODE-003", new ChatRequest { Message = "Turn two" });

        var recent = audit.GetRecent(10)
            .Where(e => e.Kind == AuditEntryKind.Chat)
            .ToList();

        Assert.Equal(2, recent.Count);
        Assert.All(recent, e => Assert.Equal(200, e.StatusCode));
        Assert.All(recent, e => Assert.Equal("INC-AUDIT", e.IncidentId));
        Assert.All(recent, e => Assert.Equal("NODE-003", e.NodeId));
    }

    [Fact]
    public async Task SeparateIncidents_DoNotShareHistory()
    {
        var (svc, store) = Build(new FakeChatHttpHandler());

        await svc.SendAsync("INC-A", "NODE-001", new ChatRequest { Message = "Turn 1 for A" });
        await svc.SendAsync("INC-B", "NODE-005", new ChatRequest { Message = "Turn 1 for B" });

        Assert.Equal(2, store.Get("INC-A")!.Messages.Count);
        Assert.Equal(2, store.Get("INC-B")!.Messages.Count);
        Assert.Equal("NODE-001", store.Get("INC-A")!.NodeId);
        Assert.Equal("NODE-005", store.Get("INC-B")!.NodeId);
    }

    [Fact]
    public async Task Delete_RemovesConversation()
    {
        var (svc, store) = Build(new FakeChatHttpHandler());
        await svc.SendAsync("INC-DEL", "NODE-001", new ChatRequest { Message = "Hello" });
        Assert.NotNull(store.Get("INC-DEL"));

        store.Delete("INC-DEL");
        Assert.Null(store.Get("INC-DEL"));
    }

    [Fact]
    public async Task EmptyMessage_IsRejectedByController()
    {
        await using var app    = TestWebHostFactory.CreateQuietFactory();
        var client             = app.CreateClient();

        var resp = await client.PostAsync(
            "/api/incidents/INC-001/chat?nodeId=NODE-003",
            new StringContent("""{"message":""}""", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Chat_NodeIdMismatchForExistingIncident_ReturnsConflict()
    {
        var store = new InMemoryConversationStore();
        store.GetOrCreate("INC-LOCK", "NODE-001");
        var chat = new TrackingChatService();
        var controller = new IncidentChatController(chat, store);

        var result = await controller.Chat(
            "INC-LOCK",
            "NODE-999",
            new ChatRequest { Message = "follow-up" },
            CancellationToken.None);

        var conflict = Assert.IsType<ConflictObjectResult>(result.Result);
        Assert.Equal(409, conflict.StatusCode);
        Assert.Equal(0, chat.CallCount);
    }

    [Fact]
    public void StoreGet_ReturnsDefensiveCopy()
    {
        var store = new InMemoryConversationStore();
        store.GetOrCreate("INC-SNAP", "NODE-101");
        store.Append("INC-SNAP", new ChatMessage { Role = "user", Content = "hello" });

        var snapshot = store.Get("INC-SNAP");
        Assert.NotNull(snapshot);
        snapshot!.Messages.Add(new ChatMessage { Role = "assistant", Content = "should not persist" });

        var reread = store.Get("INC-SNAP");
        Assert.NotNull(reread);
        Assert.Single(reread!.Messages);
    }

    [Fact]
    public void StoreAll_ReturnsDefensiveCopies()
    {
        var store = new InMemoryConversationStore();
        store.GetOrCreate("INC-A", "NODE-001");
        store.Append("INC-A", new ChatMessage { Role = "user", Content = "a1" });
        store.GetOrCreate("INC-B", "NODE-002");
        store.Append("INC-B", new ChatMessage { Role = "user", Content = "b1" });

        var all = store.All();
        Assert.Equal(2, all.Count);

        var edited = all.First(s => s.IncidentId == "INC-A");
        edited.Messages.Clear();

        var reread = store.Get("INC-A");
        Assert.NotNull(reread);
        Assert.Single(reread!.Messages);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Integration_MultiTurn_MaintainsContext()
    {
        var apiKey = Environment.GetEnvironmentVariable("Anthropic__ApiKey");
        if (string.IsNullOrEmpty(apiKey))
        {
            return; // Skip if env var not set
        }

        var config = new ConfigurationBuilder().AddEnvironmentVariables().Build();
        var store  = new InMemoryConversationStore();
        var svc    = new ChatService(new HttpClient(), new ClaudeThrottle(config), new AuditService(), config, store,
                         NullLogger<ChatService>.Instance);

        var r1 = await svc.SendAsync("INC-LIVE", "NODE-003",
            new ChatRequest { Message = "Node NODE-003 triggered a HIGH_PRESSURE alarm at 1290 PSI. Why is this concerning?" });
        var r2 = await svc.SendAsync("INC-LIVE", "NODE-003",
            new ChatRequest { Message = "What was the severity you assigned and why?" });

        // Verify both replies exist and are non-empty
        Assert.False(string.IsNullOrWhiteSpace(r1.Reply), "First reply should not be empty");
        Assert.False(string.IsNullOrWhiteSpace(r2.Reply), "Second reply should not be empty");
        
        // Verify context was maintained across turns
        Assert.Equal(2, r2.TurnCount);
        
        // Verify second response shows awareness of the alarm context
        // (mentions alarm, HIGH_PRESSURE, NODE, or severity)
        var contextKeywords = new[] { "alarm", "pressure", "node", "severity", "concern" };
        var secondReplyLower = r2.Reply.ToLowerInvariant();
        Assert.True(
            contextKeywords.Any(kw => secondReplyLower.Contains(kw)),
            $"Second reply should reference alarm context. Got: {r2.Reply}"
        );
    }
}

public sealed class TrackingChatService : IChatService
{
    public int CallCount { get; private set; }

    public Task<ChatResponse> SendAsync(
        string incidentId,
        string nodeId,
        ChatRequest request,
        CancellationToken cancellationToken = default)
    {
        CallCount++;
        return Task.FromResult(new ChatResponse
        {
            IncidentId = incidentId,
            Reply = "ok",
            TurnCount = 1,
            History = []
        });
    }
}