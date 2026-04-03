using Microsoft.AspNetCore.Mvc;
using TallgrassAgentApi.Models;
using TallgrassAgentApi.Services;
using System.Text.Json;

namespace TallgrassAgentApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class FlowController : ControllerBase
{
    private readonly IClaudeService _claudeService;

    public FlowController(IClaudeService claudeService)
    {
        _claudeService = claudeService;
    }

    [HttpPost("analyze")]
    public async Task<ActionResult<FlowResponse>> AnalyzeFlow([FromBody] FlowRequest request)
    {
        var variance = ((request.FlowRate - request.ExpectedFlowRate) / request.ExpectedFlowRate) * 100;

        var rawResponse = await _claudeService.AnalyzeFlowAsync(request);

        try
        {
            var parsed = JsonSerializer.Deserialize<JsonElement>(rawResponse);

            var result = new FlowResponse
            {
                NodeId = request.NodeId,
                PipelineSegment = request.PipelineSegment,
                Analysis = parsed.GetProperty("analysis").GetString() ?? "",
                RecommendedAction = parsed.GetProperty("recommended_action").GetString() ?? "",
                Severity = parsed.GetProperty("severity").GetString() ?? "",
                Variance = Math.Round(variance, 2)
            };

            return Ok(result);
        }
        catch
        {
            return Ok(new FlowResponse
            {
                NodeId = request.NodeId,
                PipelineSegment = request.PipelineSegment,
                Analysis = rawResponse,
                RecommendedAction = "See analysis",
                Severity = "UNKNOWN",
                Variance = Math.Round(variance, 2)
            });
        }
    }
}