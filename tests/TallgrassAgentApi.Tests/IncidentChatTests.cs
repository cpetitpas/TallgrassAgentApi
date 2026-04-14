using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
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
    private static (ChatService svc, IConversationStore store) Build(HttpMessageHandler handler)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Anthropic:ApiKey"] = "test" })
            .Build();
        var store = new InMemoryConversationStore();
        var svc   = new ChatService(new HttpClient(handler), config, store,
                        NullLogger<ChatService>.Instance);
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
        await using var app    = new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program>();
        var client             = app.CreateClient();

        var resp = await client.PostAsync(
            "/api/incidents/INC-001/chat?nodeId=NODE-003",
            new StringContent("""{"message":""}""", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact(Skip = "Requires Anthropic__ApiKey env var")]
    [Trait("Category", "Integration")]
    public async Task Integration_MultiTurn_MaintainsContext()
    {
        var config = new ConfigurationBuilder().AddEnvironmentVariables().Build();
        var store  = new InMemoryConversationStore();
        var svc    = new ChatService(new HttpClient(), config, store,
                         NullLogger<ChatService>.Instance);

        var r1 = await svc.SendAsync("INC-LIVE", "NODE-003",
            new ChatRequest { Message = "Node NODE-003 triggered a HIGH_PRESSURE alarm at 1290 PSI. Why is this concerning?" });
        var r2 = await svc.SendAsync("INC-LIVE", "NODE-003",
            new ChatRequest { Message = "What was the severity you assigned and why?" });

        // Claude should reference the prior turn in its second reply
        Assert.Contains("1290", r2.Reply + r1.Reply, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, r2.TurnCount);
    }
}