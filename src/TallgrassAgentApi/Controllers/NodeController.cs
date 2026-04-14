// Controllers/NodeController.cs
using Microsoft.AspNetCore.Mvc;
using TallgrassAgentApi.Models;
using TallgrassAgentApi.Services;

namespace TallgrassAgentApi.Controllers;

[ApiController]
[Route("api/nodes")]
public class NodeController : ControllerBase
{
    private readonly NodeHealthRegistry _registry;
    private readonly INodeClient _nodeClient;
    private readonly TelemetryChannel _channel;
    private readonly ILogger<NodeController> _logger;

    public NodeController(
        NodeHealthRegistry registry,
        INodeClient nodeClient,
        TelemetryChannel channel,
        ILogger<NodeController> logger)
    {
        _registry   = registry;
        _nodeClient = nodeClient;
        _channel    = channel;
        _logger     = logger;
    }

    // ── GET /api/nodes ────────────────────────────────────────────────────────
    /// <summary>Returns the current health state of all registered nodes.</summary>
    [HttpGet]
    public ActionResult<IEnumerable<NodeHealthEntry>> GetAll()
    {
        return Ok(_registry.All.Values.OrderBy(e => e.NodeId));
    }

    // ── GET /api/nodes/{nodeId} ───────────────────────────────────────────────
    /// <summary>Returns the current health state of a single node.</summary>
    [HttpGet("{nodeId}")]
    public ActionResult<NodeHealthEntry> GetOne(string nodeId)
    {
        var entry = _registry.Get(nodeId);
        if (entry == null)
            return NotFound($"Node '{nodeId}' has not registered with this server.");
        return Ok(entry);
    }

    // ── POST /api/nodes/{nodeId}/heartbeat ────────────────────────────────────
    /// <summary>
    /// Called by a node on its regular check-in interval.
    /// Updates the registry and publishes a HEALTH event to the SSE stream
    /// if the node is recovering from a degraded/offline state.
    /// </summary>
    [HttpPost("{nodeId}/heartbeat")]
    public async Task<ActionResult<NodeHeartbeatResponse>> Heartbeat(
        string nodeId,
        [FromBody] NodeHeartbeatRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(nodeId))
            return BadRequest("NodeId is required.");

        request.NodeId = nodeId;   // canonical — use the route value

        var before  = _registry.Get(nodeId)?.HealthState;
        var entry   = _registry.RecordHeartbeat(request);

        // Publish a recovery event if the node was previously degraded/offline
        if (before is "DEGRADED" or "OFFLINE")
        {
            _logger.LogInformation("Node {NodeId} recovered from {State}", nodeId, before);

            await _channel.Writer.WriteAsync(new TelemetryEvent
            {
                NodeId            = nodeId,
                PipelineSegment   = entry.PipelineSegment,
                EventType         = "HEALTH",
                Severity          = "LOW",
                Analysis          = $"Node {nodeId} has recovered. Heartbeat received after being {before}.",
                RecommendedAction = "No action required. Continue monitoring."
            }, ct);
        }

        return Ok(new NodeHeartbeatResponse
        {
            NodeId          = nodeId,
            Status          = "ACKNOWLEDGED",
            ServerTimestamp = DateTime.UtcNow
        });
    }

    // ── POST /api/nodes/{nodeId}/ping ─────────────────────────────────────────
    /// <summary>
    /// Ad-hoc operator-initiated health check. The server actively reaches
    /// out to the node, records the result, and publishes it to the SSE stream.
    /// </summary>
    [HttpPost("{nodeId}/ping")]
    public async Task<ActionResult<NodePingResult>> Ping(string nodeId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(nodeId))
            return BadRequest("NodeId is required.");

        _logger.LogInformation("Ad-hoc ping initiated for node {NodeId}", nodeId);

        var result = await _nodeClient.PingAsync(nodeId, ct);
        var entry  = _registry.RecordPing(result);

        var severity = result.Status switch
        {
            "OFFLINE"  => "HIGH",
            "DEGRADED" => "MEDIUM",
            _          => "LOW"
        };

        await _channel.Writer.WriteAsync(new TelemetryEvent
        {
            NodeId            = nodeId,
            PipelineSegment   = entry.PipelineSegment,
            EventType         = "PING",
            Severity          = severity,
            Analysis          = result.Reachable
                ? $"Ping to {nodeId} succeeded in {result.RoundTripMs}ms. " +
                  $"Status: {result.Status}. Firmware: {result.FirmwareVersion ?? "unknown"}."
                : $"Ping to {nodeId} failed. {result.ErrorMessage}",
            RecommendedAction = result.Status switch
            {
                "OFFLINE"  => "Node is not responding. Verify power and network connectivity.",
                "DEGRADED" => "Node responded but with degraded signal or low battery. Schedule maintenance.",
                _          => "Node is healthy. No action required."
            },
            CurrentValue = result.RoundTripMs.HasValue ? (double?)result.RoundTripMs : null,
            Unit         = result.RoundTripMs.HasValue ? "ms" : null
        }, ct);

        return Ok(result);
    }
}
