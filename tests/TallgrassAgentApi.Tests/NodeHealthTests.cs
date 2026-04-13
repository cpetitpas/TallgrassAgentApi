using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using TallgrassAgentApi.Models;
using TallgrassAgentApi.Services;

namespace TallgrassAgentApi.Tests;

/// <summary>
/// Tests for node health endpoints and registry.
/// Uses TestBase for endpoint tests (FakeClaudeService injected).
/// Uses a factory-aware class for tests that need DI service access.
/// </summary>
public class NodeHeartbeatTests : TestBase
{
    public NodeHeartbeatTests(WebApplicationFactory<Program> factory) : base(factory) { }

    [Fact]
    public async Task Heartbeat_ReturnsOk()
    {
        var request = GetHeartbeatRequest("NODE-001");
        var response = await PostAsync("/api/nodes/NODE-001/heartbeat", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Heartbeat_Response_IsAcknowledged()
    {
        var request = GetHeartbeatRequest("NODE-002");
        var result = await PostAndDeserialize<NodeHeartbeatResponse>("/api/nodes/NODE-002/heartbeat", request);
        Assert.Equal("ACKNOWLEDGED", result.Status);
        Assert.Equal("NODE-002", result.NodeId);
    }

    [Fact]
    public async Task Heartbeat_Response_HasServerTimestamp()
    {
        var request = GetHeartbeatRequest("NODE-003");
        var result = await PostAndDeserialize<NodeHeartbeatResponse>("/api/nodes/NODE-003/heartbeat", request);
        Assert.True(result.ServerTimestamp > DateTime.UtcNow.AddMinutes(-1));
    }

    public static NodeHeartbeatRequest GetHeartbeatRequest(string nodeId) => new()
    {
        NodeId          = nodeId,
        PipelineSegment = "SEG-TEST",
        FirmwareVersion = "2.4.1",
        SignalStrength  = -62.5,
        BatteryPercent  = 88.0,
        Timestamp       = DateTime.UtcNow
    };
}

public class NodePingTests : TestBase
{
    public NodePingTests(WebApplicationFactory<Program> factory) : base(factory) { }

    [Fact]
    public async Task Ping_ReturnsOk()
    {
        var response = await Client.PostAsync("/api/nodes/NODE-001/ping", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Ping_Result_HasNodeId()
    {
        var result = await PostAndDeserialize<NodePingResult>("/api/nodes/NODE-001/ping", new { });
        Assert.Equal("NODE-001", result.NodeId);
    }

    [Fact]
    public async Task Ping_Result_HasStatus()
    {
        var result = await PostAndDeserialize<NodePingResult>("/api/nodes/NODE-001/ping", new { });
        var valid = new[] { "ONLINE", "DEGRADED", "OFFLINE" };
        Assert.Contains(result.Status, valid);
    }

    [Fact]
    public async Task Ping_OnlineNode_IsReachable()
    {
        // NODE-001 last char '1' → SimulatedNodeClient returns ONLINE
        var result = await PostAndDeserialize<NodePingResult>("/api/nodes/NODE-001/ping", new { });
        Assert.True(result.Reachable);
        Assert.NotNull(result.RoundTripMs);
    }

    [Fact]
    public async Task Ping_OfflineNode_IsNotReachable()
    {
        // NODE-010 last char '0' → SimulatedNodeClient returns OFFLINE
        var result = await PostAndDeserialize<NodePingResult>("/api/nodes/NODE-010/ping", new { });
        Assert.False(result.Reachable);
        Assert.Equal("OFFLINE", result.Status);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public async Task Ping_DegradedNode_ReturnsDegraded()
    {
        // NODE-005 last char '5' → SimulatedNodeClient returns DEGRADED
        var result = await PostAndDeserialize<NodePingResult>("/api/nodes/NODE-005/ping", new { });
        Assert.Equal("DEGRADED", result.Status);
    }

    // Ping uses HttpClient.PostAsync with null body — need a helper for that
    private new async Task<T> PostAndDeserialize<T>(string url, object _)
    {
        var response = await Client.PostAsync(url, null);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<T>();
        Assert.NotNull(result);
        return result!;
    }
}

public class NodeRegistryTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public NodeRegistryTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                var d = services.SingleOrDefault(s => s.ServiceType == typeof(IClaudeService));
                if (d != null) services.Remove(d);
                services.AddScoped<IClaudeService, FakeClaudeService>();
            });
        });
        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task GetAll_BeforeAnyHeartbeats_ReturnsEmptyOrExisting()
    {
        var response = await _client.GetAsync("/api/nodes");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetOne_AfterHeartbeat_ReturnsHealthyEntry()
    {
        var registry = _factory.Services.GetRequiredService<NodeHealthRegistry>();
        registry.RecordHeartbeat(new NodeHeartbeatRequest
        {
            NodeId          = "NODE-REG-001",
            PipelineSegment = "SEG-A",
            FirmwareVersion = "2.4.1",
            Timestamp       = DateTime.UtcNow
        });

        var response = await _client.GetAsync("/api/nodes/NODE-REG-001");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var entry = await response.Content.ReadFromJsonAsync<NodeHealthEntry>();
        Assert.NotNull(entry);
        Assert.Equal("HEALTHY", entry!.HealthState);
        Assert.Equal("NODE-REG-001", entry.NodeId);
    }

    [Fact]
    public async Task GetOne_UnknownNode_ReturnsNotFound()
    {
        var response = await _client.GetAsync("/api/nodes/NODE-DOES-NOT-EXIST");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Registry_RecordHeartbeat_SetsHealthy()
    {
        var registry = _factory.Services.GetRequiredService<NodeHealthRegistry>();
        var entry = registry.RecordHeartbeat(new NodeHeartbeatRequest
        {
            NodeId    = "NODE-RH-001",
            Timestamp = DateTime.UtcNow
        });
        Assert.Equal("HEALTHY", entry.HealthState);
        Assert.Equal(0, entry.MissedIntervals);
    }

    [Fact]
    public async Task Registry_OneMissedInterval_SetsDegraded()
    {
        var registry = _factory.Services.GetRequiredService<NodeHealthRegistry>();
        registry.RecordHeartbeat(new NodeHeartbeatRequest
        {
            NodeId    = "NODE-MI-001",
            Timestamp = DateTime.UtcNow
        });

        var entry = registry.RecordMissedInterval("NODE-MI-001");
        Assert.Equal("DEGRADED", entry.HealthState);
        Assert.Equal(1, entry.MissedIntervals);
    }

    [Fact]
    public async Task Registry_TwoMissedIntervals_SetsOffline()
    {
        var registry = _factory.Services.GetRequiredService<NodeHealthRegistry>();
        registry.RecordHeartbeat(new NodeHeartbeatRequest
        {
            NodeId    = "NODE-MI-002",
            Timestamp = DateTime.UtcNow
        });

        registry.RecordMissedInterval("NODE-MI-002");
        var entry = registry.RecordMissedInterval("NODE-MI-002");
        Assert.Equal("OFFLINE", entry.HealthState);
        Assert.Equal(2, entry.MissedIntervals);
    }

    [Fact]
    public async Task Registry_HeartbeatAfterDegraded_ResetsToHealthy()
    {
        var registry = _factory.Services.GetRequiredService<NodeHealthRegistry>();
        registry.RecordHeartbeat(new NodeHeartbeatRequest { NodeId = "NODE-REC-001", Timestamp = DateTime.UtcNow });
        registry.RecordMissedInterval("NODE-REC-001");

        Assert.Equal("DEGRADED", registry.Get("NODE-REC-001")!.HealthState);

        registry.RecordHeartbeat(new NodeHeartbeatRequest { NodeId = "NODE-REC-001", Timestamp = DateTime.UtcNow });

        var entry = registry.Get("NODE-REC-001");
        Assert.Equal("HEALTHY", entry!.HealthState);
        Assert.Equal(0, entry.MissedIntervals);
    }

    [Fact]
    public async Task Ping_AfterHeartbeat_UpdatesLastPing()
    {
        var registry = _factory.Services.GetRequiredService<NodeHealthRegistry>();
        registry.RecordHeartbeat(new NodeHeartbeatRequest { NodeId = "NODE-PING-001", Timestamp = DateTime.UtcNow });

        registry.RecordPing(new NodePingResult
        {
            NodeId      = "NODE-PING-001",
            Reachable   = true,
            RoundTripMs = 42,
            Status      = "ONLINE",
            Timestamp   = DateTime.UtcNow
        });

        var entry = registry.Get("NODE-PING-001");
        Assert.NotNull(entry!.LastPing);
        Assert.Equal(42, entry.LastRoundTripMs);
    }
}

public class NodeHeartbeatSseTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public NodeHeartbeatSseTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                var d = services.SingleOrDefault(s => s.ServiceType == typeof(IClaudeService));
                if (d != null) services.Remove(d);
                services.AddScoped<IClaudeService, FakeClaudeService>();
            });
        });
        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task Ping_PublishesEventToSseStream()
    {
        var channel = _factory.Services.GetRequiredService<TelemetryChannel>();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var sseResponse = await _client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/api/stream/events"),
            HttpCompletionOption.ResponseHeadersRead,
            cts.Token);

        await using var stream = await sseResponse.Content.ReadAsStreamAsync(cts.Token);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        // Trigger a ping — SimulatedNodeClient responds synchronously-ish
        await _client.PostAsync("/api/nodes/NODE-001/ping", null, cts.Token);

        string? dataLine = null;
        while (!cts.Token.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cts.Token);
            if (line?.StartsWith("data:") == true && line.Contains("\"PING\""))
            {
                dataLine = line["data:".Length..].Trim();
                break;
            }
        }

        Assert.NotNull(dataLine);
        Assert.Contains("PING", dataLine);
    }

    [Fact]
    public async Task Heartbeat_RecoveryPublishesEventToSseStream()
    {
        var registry = _factory.Services.GetRequiredService<NodeHealthRegistry>();

        // Pre-mark the node as DEGRADED so the heartbeat triggers a recovery event
        registry.RecordHeartbeat(new NodeHeartbeatRequest { NodeId = "NODE-SSE-REC", Timestamp = DateTime.UtcNow.AddMinutes(-5) });
        registry.RecordMissedInterval("NODE-SSE-REC");
        Assert.Equal("DEGRADED", registry.Get("NODE-SSE-REC")!.HealthState);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var sseResponse = await _client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/api/stream/events"),
            HttpCompletionOption.ResponseHeadersRead,
            cts.Token);

        await using var stream = await sseResponse.Content.ReadAsStreamAsync(cts.Token);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        var heartbeat = new StringContent(
            System.Text.Json.JsonSerializer.Serialize(new NodeHeartbeatRequest
            {
                NodeId          = "NODE-SSE-REC",
                PipelineSegment = "SEG-A",
                FirmwareVersion = "2.4.1",
                Timestamp       = DateTime.UtcNow
            }),
            Encoding.UTF8, "application/json");

        await _client.PostAsync("/api/nodes/NODE-SSE-REC/heartbeat", heartbeat, cts.Token);

        string? dataLine = null;
        while (!cts.Token.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cts.Token);
            if (line?.StartsWith("data:") == true && line.Contains("NODE-SSE-REC"))
            {
                dataLine = line["data:".Length..].Trim();
                break;
            }
        }

        Assert.NotNull(dataLine);
        Assert.Contains("NODE-SSE-REC", dataLine);
        Assert.Contains("HEALTH", dataLine);
    }
}
