using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using TallgrassAgentApi.Models;
using TallgrassAgentApi.Services;
using Xunit;

namespace TallgrassAgentApi.Tests;

public class AuditServiceTests
{
    private static AuditService Build() => new();

    [Fact]
    public void Record_And_GetRecent_ReturnNewestFirst()
    {
        var svc = Build();
        for (int i = 1; i <= 5; i++)
            svc.Record(new AuditEntry { Kind = AuditEntryKind.Alarm, NodeId = $"NODE-{i:D3}" });

        var recent = svc.GetRecent(5);
        Assert.Equal(5, recent.Count);
        Assert.Equal("NODE-005", recent[0].NodeId);  // newest first
    }

    [Fact]
    public void Buffer_CapsAt1000()
    {
        var svc = Build();
        for (int i = 0; i < 1100; i++)
            svc.Record(new AuditEntry { Kind = AuditEntryKind.Alarm });

        Assert.Equal(1000, svc.GetRecent(2000).Count);
    }

    [Fact]
    public void Summary_AggregatesCorrectly()
    {
        var svc = Build();
        svc.Record(new AuditEntry { Kind = AuditEntryKind.Alarm,       InputTokens = 100, OutputTokens = 50,  ElapsedMs = 200, StatusCode = 200 });
        svc.Record(new AuditEntry { Kind = AuditEntryKind.Investigate, InputTokens = 300, OutputTokens = 120, ElapsedMs = 800, StatusCode = 200 });
        svc.Record(new AuditEntry { Kind = AuditEntryKind.Chat,        InputTokens = 150, OutputTokens = 60,  ElapsedMs = 400, StatusCode = 500 });

        var summary = svc.GetSummary();

        Assert.Equal(3,    summary.TotalCalls);
        Assert.Equal(550,  summary.TotalInputTokens);
        Assert.Equal(230,  summary.TotalOutputTokens);
        Assert.Equal(780,  summary.TotalTokens);
        Assert.Equal(1,    summary.ErrorCount);
        Assert.Equal(1, summary.CallsByKind["Alarm"]);
        Assert.Equal(0, summary.CallsByKind["Flow"]);
        Assert.Equal(0, summary.CallsByKind["MultiNode"]);
        Assert.Equal(1, summary.CallsByKind["Investigate"]);
        Assert.Equal(1, summary.CallsByKind["Chat"]);
    }

    [Fact]
    public void PromptHasher_IsDeterministic()
    {
        var h1 = PromptHasher.Hash("NODE-003HIGH_PRESSURE1290");
        var h2 = PromptHasher.Hash("NODE-003HIGH_PRESSURE1290");
        var h3 = PromptHasher.Hash("NODE-003HIGH_PRESSURE1291");

        Assert.Equal(h1, h2);
        Assert.NotEqual(h1, h3);
        Assert.Equal(16, h1.Length);
    }

    [Fact]
    public async Task AuditEndpoints_ReturnOk()
    {
        await using var app = new WebApplicationFactory<Program>();
        var client = app.CreateClient();

        var r1 = await client.GetAsync("/api/audit");
        var r2 = await client.GetAsync("/api/audit/summary");

        Assert.Equal(HttpStatusCode.OK, r1.StatusCode);
        Assert.Equal(HttpStatusCode.OK, r2.StatusCode);
    }

    [Fact]
    public async Task AuditSummary_ReflectsRecordedEntries()
    {
        await using var app = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
                builder.ConfigureServices(services =>
                {
                    // Replace singleton with a pre-populated one
                    var svc = new AuditService();
                    svc.Record(new AuditEntry { Kind = AuditEntryKind.Alarm, InputTokens = 200, OutputTokens = 80, ElapsedMs = 300, StatusCode = 200 });
                    services.AddSingleton<IAuditService>(svc);
                }));

        var client = app.CreateClient();
        var resp   = await client.GetAsync("/api/audit/summary");
        var body   = await resp.Content.ReadAsStringAsync();

        using var doc = JsonDocument.Parse(body);
        Assert.Equal(1, doc.RootElement.GetProperty("totalCalls").GetInt32());
        Assert.Equal(280, doc.RootElement.GetProperty("totalTokens").GetInt32());

        var callsByKind = doc.RootElement.GetProperty("callsByKind");
        Assert.Equal(1, callsByKind.GetProperty("Alarm").GetInt32());
        Assert.Equal(0, callsByKind.GetProperty("Flow").GetInt32());
        Assert.Equal(0, callsByKind.GetProperty("MultiNode").GetInt32());
        Assert.Equal(0, callsByKind.GetProperty("Investigate").GetInt32());
        Assert.Equal(0, callsByKind.GetProperty("Chat").GetInt32());
    }
}