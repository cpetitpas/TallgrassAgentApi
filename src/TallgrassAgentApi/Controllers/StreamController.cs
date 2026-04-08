// Controllers/StreamController.cs
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using TallgrassAgentApi.Models;
using TallgrassAgentApi.Services;

namespace TallgrassAgentApi.Controllers;

[ApiController]
[Route("api/stream")]
public class StreamController : ControllerBase
{
    private readonly TelemetryChannel _channel;
    private readonly ILogger<StreamController> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public StreamController(TelemetryChannel channel, ILogger<StreamController> logger)
    {
        _channel = channel;
        _logger = logger;
    }

    /// <summary>
    /// Server-Sent Events stream. Connect with the browser's native EventSource API.
    /// Each event is a JSON-serialised TelemetryEvent named "telemetry".
    /// A heartbeat comment ": ping" is sent every 15 seconds to keep the connection alive.
    /// </summary>
    [HttpGet("events")]
    public async Task GetEvents(CancellationToken cancellationToken)
    {
        Response.Headers.Append("Content-Type", "text/event-stream");
        Response.Headers.Append("Cache-Control", "no-cache");
        Response.Headers.Append("X-Accel-Buffering", "no");   // disable nginx buffering

        _logger.LogInformation("SSE client connected from {IP}", HttpContext.Connection.RemoteIpAddress);

        using var heartbeatTimer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        var heartbeatTask = HeartbeatAsync(heartbeatTimer, cancellationToken);

        try
        {
            await foreach (var evt in _channel.Reader.ReadAllAsync(cancellationToken))
            {
                var json = JsonSerializer.Serialize(evt, JsonOpts);
                var payload = $"event: telemetry\ndata: {json}\n\n";
                var bytes = Encoding.UTF8.GetBytes(payload);
                await Response.Body.WriteAsync(bytes, cancellationToken);
                await Response.Body.FlushAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Client disconnected — normal shutdown
        }
        finally
        {
            _logger.LogInformation("SSE client disconnected.");
        }

        await heartbeatTask;
    }

    private async Task HeartbeatAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                var ping = Encoding.UTF8.GetBytes(": ping\n\n");
                await Response.Body.WriteAsync(ping, ct);
                await Response.Body.FlushAsync(ct);
            }
        }
        catch (OperationCanceledException) { }
    }
}
