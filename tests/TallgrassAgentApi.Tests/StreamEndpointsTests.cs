using System.Net;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using TallgrassAgentApi.Models;
using TallgrassAgentApi.Services;

namespace TallgrassAgentApi.Tests;

/// <summary>
/// Tests for GET /api/stream/events (SSE).
/// Does not inherit TestBase because SSE tests need both an HTTP client AND
/// direct access to DI services (TelemetryChannel) to inject events.
/// The same FakeClaudeService swap from TestBase is replicated here.
/// </summary>
public class StreamEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public StreamEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                var descriptor = services.SingleOrDefault(
                    d => d.ServiceType == typeof(IClaudeService));
                if (descriptor != null)
                    services.Remove(descriptor);
                services.AddScoped<IClaudeService, FakeClaudeService>();
            });
        });

        _client = _factory.CreateClient();
    }

    // ── Content-type ─────────────────────────────────────────────────────────

    [Fact]
    public async Task StreamEvents_ReturnsTextEventStream_ContentType()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var response = await _client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/api/stream/events"),
            HttpCompletionOption.ResponseHeadersRead,
            cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
    }

    // ── Event appears in stream ───────────────────────────────────────────────

    [Fact]
    public async Task StreamEvents_PublishedEvent_AppearsInStream()
    {
        var channel = _factory.Services.GetRequiredService<TelemetryChannel>();

        var expected = new TelemetryEvent
        {
            EventId           = "TESTEVT1",
            NodeId            = "NODE-TEST",
            PipelineSegment   = "SEG-TEST",
            EventType         = "ALARM",
            Severity          = "HIGH",
            Analysis          = "Test analysis",
            RecommendedAction = "Test action",
            CurrentValue      = 900.0,
            Threshold         = 800.0,
            Unit              = "PSI"
        };

        // Open the stream first, then publish — avoids a race where the event
        // is written to the channel before any reader is listening.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var response = await _client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/api/stream/events"),
            HttpCompletionOption.ResponseHeadersRead,
            cts.Token);

        await using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        await channel.Writer.WriteAsync(expected, cts.Token);

        string? dataLine = null;
        while (!cts.Token.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cts.Token);
            if (line == null) break;
            if (line.StartsWith("data:") && line.Contains("TESTEVT1"))
            {
                dataLine = line["data:".Length..].Trim();
                break;
            }
        }

        Assert.NotNull(dataLine);
        Assert.Contains("TESTEVT1", dataLine);
        Assert.Contains("NODE-TEST", dataLine);
    }

    // ── Expected JSON fields are present ─────────────────────────────────────

    [Fact]
    public async Task StreamEvents_PublishedEvent_ContainsExpectedFields()
    {
        var channel = _factory.Services.GetRequiredService<TelemetryChannel>();

        var evt = new TelemetryEvent
        {
            EventId           = "TESTEVT2",
            NodeId            = "NODE-042",
            EventType         = "FLOW",
            Severity          = "MEDIUM",
            Analysis          = "Flow variance detected",
            RecommendedAction = "Monitor closely",
            VariancePercent   = 18.5
        };

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var response = await _client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/api/stream/events"),
            HttpCompletionOption.ResponseHeadersRead,
            cts.Token);

        await using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        await channel.Writer.WriteAsync(evt, cts.Token);

        string? dataLine = null;
        while (!cts.Token.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cts.Token);
            if (line?.StartsWith("data:") == true && line.Contains("TESTEVT2"))
            {
                dataLine = line["data:".Length..].Trim();
                break;
            }
        }

        Assert.NotNull(dataLine);
        Assert.Contains("\"eventType\"",        dataLine);
        Assert.Contains("\"severity\"",         dataLine);
        Assert.Contains("\"analysis\"",         dataLine);
        Assert.Contains("\"recommendedAction\"", dataLine);
        Assert.Contains("FLOW",                 dataLine);
        Assert.Contains("MEDIUM",               dataLine);
    }

    // ── SSE wire format: event name line ─────────────────────────────────────

    [Fact]
    public async Task StreamEvents_PublishedEvent_HasTelemetryEventName()
    {
        var channel = _factory.Services.GetRequiredService<TelemetryChannel>();

        var evt = new TelemetryEvent
        {
            EventId   = "TESTEVT3",
            NodeId    = "NODE-001",
            EventType = "ALARM",
            Severity  = "LOW"
        };

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var response = await _client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/api/stream/events"),
            HttpCompletionOption.ResponseHeadersRead,
            cts.Token);

        await using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        await channel.Writer.WriteAsync(evt, cts.Token);

        // Collect lines for one complete SSE message (terminated by blank line)
        var lines = new List<string>();
        while (!cts.Token.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cts.Token);
            if (line == null) break;
            if (line == string.Empty && lines.Count > 0) break;
            if (line.Length > 0) lines.Add(line);
        }

        Assert.Contains(lines, l => l == "event: telemetry");
        Assert.Contains(lines, l => l.StartsWith("data:"));
    }

    // ── Multiple events all arrive ────────────────────────────────────────────

    [Fact]
    public async Task StreamEvents_MultiplePublishedEvents_AllAppearInStream()
    {
        var channel = _factory.Services.GetRequiredService<TelemetryChannel>();

        var ids = new[] { "MULTI001", "MULTI002", "MULTI003" };

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var response = await _client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/api/stream/events"),
            HttpCompletionOption.ResponseHeadersRead,
            cts.Token);

        await using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        foreach (var id in ids)
            await channel.Writer.WriteAsync(
                new TelemetryEvent { EventId = id, NodeId = "NODE-001", EventType = "ALARM", Severity = "LOW" },
                cts.Token);

        var found = new HashSet<string>();
        while (found.Count < ids.Length && !cts.Token.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cts.Token);
            if (line == null) break;
            foreach (var id in ids)
                if (line.Contains(id)) found.Add(id);
        }

        Assert.Equal(ids.Length, found.Count);
    }
}