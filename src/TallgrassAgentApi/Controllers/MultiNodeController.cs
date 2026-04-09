using Microsoft.AspNetCore.Mvc;
using TallgrassAgentApi.Models;
using TallgrassAgentApi.Services;
using System.Text.Json;
using System.Threading;

namespace TallgrassAgentApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class MultiNodeController : ControllerBase
{
    private readonly IClaudeService _claudeService;

    public MultiNodeController(IClaudeService claudeService)
    {
        _claudeService = claudeService;
    }

    [HttpPost("analyze")]
    public async Task<ActionResult<MultiNodeResponse>> AnalyzeMultiNode([FromBody] MultiNodeRequest request, CancellationToken cancellationToken)
    {
        if (request.Readings == null || request.Readings.Count == 0)
            return BadRequest("At least one node reading is required.");

        var rawResponse = await _claudeService.AnalyzeMultiNodeAsync(request, cancellationToken);

        try
        {
            var parsed = JsonSerializer.Deserialize<JsonElement>(rawResponse);

            // Count statuses from the incoming readings
            var criticalCount = request.Readings.Count(r => r.Status == "CRITICAL");
            var warningCount = request.Readings.Count(r => r.Status == "WARNING");
            var normalCount = request.Readings.Count(r => r.Status == "NORMAL");

            // Parse affected_nodes array from Claude's response
            var affectedNodes = new List<string>();
            if (parsed.TryGetProperty("affected_nodes", out var nodesElement))
            {
                affectedNodes = nodesElement.EnumerateArray()
                    .Select(n => n.GetString() ?? "")
                    .Where(n => !string.IsNullOrEmpty(n))
                    .ToList();
            }

            var result = new MultiNodeResponse
            {
                RegionId = request.RegionId,
                TotalNodes = request.Readings.Count,
                CriticalCount = criticalCount,
                WarningCount = warningCount,
                NormalCount = normalCount,
                OverallStatus = parsed.GetProperty("overall_status").GetString() ?? "",
                Summary = parsed.GetProperty("summary").GetString() ?? "",
                RecommendedAction = parsed.GetProperty("recommended_action").GetString() ?? "",
                AffectedNodes = affectedNodes
            };

            return Ok(result);
        }
        catch
        {
            return Ok(new MultiNodeResponse
            {
                RegionId = request.RegionId,
                TotalNodes = request.Readings.Count,
                Summary = rawResponse,
                RecommendedAction = "See summary",
                OverallStatus = "UNKNOWN"
            });
        }
    }
}