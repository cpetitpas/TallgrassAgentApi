using Microsoft.AspNetCore.Mvc;
using TallgrassAgentApi.Models;
using TallgrassAgentApi.Services;

namespace TallgrassAgentApi.Controllers;

[ApiController]
[Route("api/alarm/investigate")]
public class MultiNodeInvestigateController : ControllerBase
{
    private readonly IMultiNodeInvestigateService _svc;

    public MultiNodeInvestigateController(IMultiNodeInvestigateService svc) => _svc = svc;

    /// <summary>POST /api/alarm/investigate/multinode</summary>
    [HttpPost("multinode")]
    public async Task<ActionResult<MultiNodeInvestigateResponse>> Investigate(
        [FromBody] MultiNodeInvestigateRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Nodes is null || request.Nodes.Count < 2)
            return BadRequest("At least 2 nodes are required.");

        if (request.Nodes.Count > 10)
            return BadRequest("Maximum 10 nodes per multi-node investigation.");

        if (request.Nodes.Any(n => string.IsNullOrWhiteSpace(n.NodeId)))
            return BadRequest("All nodes must have a NodeId.");

        if (request.Nodes.Any(n => string.IsNullOrWhiteSpace(n.AlarmType)))
            return BadRequest("All nodes must have an AlarmType.");

        if (request.Nodes.Any(n => string.IsNullOrWhiteSpace(n.Unit)))
            return BadRequest("All nodes must have a Unit.");

        if (request.Nodes.Any(n => double.IsNaN(n.SensorValue) || double.IsInfinity(n.SensorValue)))
            return BadRequest("All nodes must have a valid SensorValue.");

        var normalizedNodes = request.Nodes
            .Select(n => n with
            {
                NodeId = n.NodeId.Trim(),
                AlarmType = n.AlarmType.Trim(),
                Unit = n.Unit.Trim()
            })
            .ToList();

        var seenNodeIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (normalizedNodes.Any(n => !seenNodeIds.Add(n.NodeId)))
            return BadRequest("Duplicate NodeId values are not allowed.");

        var normalizedRequest = request with { Nodes = normalizedNodes };

        try
        {
            var result = await _svc.InvestigateAsync(normalizedRequest, cancellationToken);
            return Ok(result);
        }
        catch (ThrottleRejectedException ex)
        {
            return StatusCode(503, ex.Message);
        }
        catch (HttpRequestException ex)
        {
            return StatusCode(502, ex.Message);
        }
    }
}