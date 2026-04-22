using Microsoft.AspNetCore.Mvc;
using TallgrassAgentApi.Models;
using TallgrassAgentApi.Services;

namespace TallgrassAgentApi.Controllers;

[ApiController]
[Route("api/alarm")]
public class InvestigateController : ControllerBase
{
    private readonly IInvestigateService _svc;

    public InvestigateController(IInvestigateService svc) => _svc = svc;

    [HttpPost("investigate")]
    public async Task<ActionResult<InvestigateResponse>> Investigate(
        [FromBody] InvestigateRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.NodeId))
            return BadRequest("NodeId is required.");
        if (string.IsNullOrWhiteSpace(request.AlarmType))
            return BadRequest("AlarmType is required.");

        try
        {
            var result = await _svc.InvestigateAsync(request, cancellationToken);
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