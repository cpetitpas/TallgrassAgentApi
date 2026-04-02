using Microsoft.AspNetCore.Mvc;
using TallgrassAgentApi.Models;
using TallgrassAgentApi.Services;
using System.Text.Json;

namespace TallgrassAgentApi.Controllers;

[ApiController]
[Route("api/[controller]")]  // endpoint will be: POST /api/alarm/analyze
public class AlarmController : ControllerBase
{
    private readonly ClaudeService _claudeService;

    // ASP.NET "injects" ClaudeService here automatically (registered in Program.cs)
    public AlarmController(ClaudeService claudeService)
    {
        _claudeService = claudeService;
    }

    [HttpPost("analyze")]
    public async Task<ActionResult<AlarmResponse>> AnalyzeAlarm([FromBody] AlarmRequest request)
    {
        // Call Claude via our service
        var rawResponse = await _claudeService.AnalyzeAlarmAsync(request);

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
public class FlowRequest
{
    public string NodeId { get; set; } = string.Empty;
    public string PipelineSegment { get; set; } = string.Empty;  // e.g. "SEG-7A"
    public double FlowRate { get; set; }                          // e.g. 142.7
    public double ExpectedFlowRate { get; set; }                  // e.g. 150.0
    public string Unit { get; set; } = string.Empty;              // e.g. "MMSCFD"
    public string FlowDirection { get; set; } = string.Empty;     // e.g. "FORWARD" / "REVERSE"
    public DateTime Timestamp { get; set; }
}

public class FlowResponse
{
    public string NodeId { get; set; } = string.Empty;
    public string PipelineSegment { get; set; } = string.Empty;
    public string Analysis { get; set; } = string.Empty;
    public string RecommendedAction { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public double Variance { get; set; }                          // % difference from expected
}