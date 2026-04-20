using Microsoft.AspNetCore.Mvc;
using TallgrassAgentApi.Models;
using TallgrassAgentApi.Services;
using System.Text.Json;
using System.Threading;

namespace TallgrassAgentApi.Controllers;

[ApiController]
[Route("api/[controller]")]  // endpoint will be: POST /api/alarm/analyze
public class AlarmController : ControllerBase
{
    private readonly IClaudeService _claudeService;

    // ASP.NET "injects" ClaudeService here automatically (registered in Program.cs)
    public AlarmController(IClaudeService claudeService)
    {
        _claudeService = claudeService;
    }

    [HttpPost("analyze")]
    public async Task<ActionResult<AlarmResponse>> AnalyzeAlarm([FromBody] AlarmRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.NodeId))
        {
            return BadRequest("NodeId is required.");
        }

        string rawResponse;
        try
        {
            // Call Claude via our service
            rawResponse = await _claudeService.AnalyzeAlarmAsync(request, cancellationToken);
        }
        catch (ThrottleRejectedException ex)
        {
            return StatusCode(503, ex.Message);
        }
        catch (HttpRequestException ex)
        {
            return StatusCode(502, ex.Message);
        }

        // Claude was told to return JSON — parse it into our response model
        try
        {
            var parsed = JsonSerializer.Deserialize<JsonElement>(rawResponse);

            var result = new AlarmResponse
            {
                NodeId = request.NodeId,
                Analysis = parsed.GetProperty("analysis").GetString() ?? "",
                RecommendedAction = parsed.GetProperty("recommended_action").GetString() ?? "",
                Severity = parsed.GetProperty("severity").GetString() ?? ""
            };

            return Ok(result);
        }
        catch
        {
            // If Claude didn't return clean JSON for some reason, return raw text
            return Ok(new AlarmResponse
            {
                NodeId = request.NodeId,
                Analysis = rawResponse,
                RecommendedAction = "See analysis",
                Severity = "UNKNOWN"
            });
        }
    }
}
