using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using TallgrassAgentApi.Services;
using Xunit;

namespace TallgrassAgentApi.Tests;

public class DiagnosticsTests
{
    private static ClaudeThrottle BuildThrottle(int max = 3, int waitMs = 500)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ClaudeThrottle:MaxConcurrent"] = max.ToString(),
                ["ClaudeThrottle:MaxWaitMs"]     = waitMs.ToString()
            })
            .Build();
        return new ClaudeThrottle(config);
    }

    [Fact]
    public async Task Acquire_UpToMax_Succeeds()
    {
        var throttle = BuildThrottle(max: 3);
        var slots = new List<IDisposable>();

        for (int i = 0; i < 3; i++)
            slots.Add(await throttle.AcquireAsync());

        var snap = throttle.Snapshot();
        Assert.Equal(3, snap.ActiveCalls);
        Assert.True(snap.IsThrottled);

        slots.ForEach(s => s.Dispose());
    }

    [Fact]
    public async Task Acquire_BeyondMax_Rejects_AfterTimeout()
    {
        var throttle = BuildThrottle(max: 2, waitMs: 200);

        var slot1 = await throttle.AcquireAsync();
        var slot2 = await throttle.AcquireAsync();

        // Third call should reject after 200ms
        await Assert.ThrowsAsync<ThrottleRejectedException>(
            () => throttle.AcquireAsync());

        Assert.Equal(1, throttle.Snapshot().RejectedCalls);

        slot1.Dispose();
        slot2.Dispose();
    }

    [Fact]
    public async Task Release_DecrementsActive_IncrementsCompleted()
    {
        var throttle = BuildThrottle(max: 3);

        var slot = await throttle.AcquireAsync();
        Assert.Equal(1, throttle.Snapshot().ActiveCalls);

        slot.Dispose();

        var snap = throttle.Snapshot();
        Assert.Equal(0, snap.ActiveCalls);
        Assert.Equal(1, snap.CompletedCalls);
    }

    [Fact]
    public async Task Waiting_Counter_Tracks_Queued_Callers()
    {
        var throttle = BuildThrottle(max: 1, waitMs: 2000);

        var slot = await throttle.AcquireAsync();

        // Start a second acquire in background — it will wait
        var waiterTask = Task.Run(async () => await throttle.AcquireAsync());
        await Task.Delay(50); // give it time to enter the wait

        Assert.Equal(1, throttle.Snapshot().WaitingCalls);

        slot.Dispose(); // release — waiter should now acquire
        var waiterSlot = await waiterTask;
        waiterSlot.Dispose();
    }

    [Fact]
    public async Task DiagnosticsEndpoint_ReturnsOk()
    {
        await using var app = new WebApplicationFactory<Program>();
        var client = app.CreateClient();

        var r1 = await client.GetAsync("/api/diagnostics/queue");
        var r2 = await client.GetAsync("/api/diagnostics");

        Assert.Equal(HttpStatusCode.OK, r1.StatusCode);
        Assert.Equal(HttpStatusCode.OK, r2.StatusCode);
    }

    [Fact]
    public async Task DiagnosticsQueue_ReflectsConfig()
    {
        await using var app = new WebApplicationFactory<Program>();
        var client = app.CreateClient();

        var resp = await client.GetAsync("/api/diagnostics/queue");
        var body = await resp.Content.ReadAsStringAsync();

        using var doc = JsonDocument.Parse(body);
        // MaxConcurrent should match appsettings value of 3
        Assert.Equal(3, doc.RootElement.GetProperty("maxConcurrent").GetInt32());
        Assert.False(doc.RootElement.GetProperty("isThrottled").GetBoolean());
    }
}