using Microsoft.AspNetCore.Mvc;
using TallgrassAgentApi.Models;
using TallgrassAgentApi.Services;

namespace TallgrassAgentApi.Controllers;

[ApiController]
[Route("api/diagnostics")]
public class DiagnosticsController : ControllerBase
{
    private readonly ClaudeThrottle  _throttle;
    private readonly IAuditService   _audit;

    public DiagnosticsController(ClaudeThrottle throttle, IAuditService audit)
    {
        _throttle = throttle;
        _audit    = audit;
    }

    /// <summary>GET /api/diagnostics/queue</summary>
    [HttpGet("queue")]
    public ActionResult<QueueSnapshot> GetQueue()
        => Ok(_throttle.Snapshot());

    /// <summary>GET /api/diagnostics — combined snapshot with audit summary</summary>
    [HttpGet]
    public ActionResult<object> GetAll()
        => Ok(new { queue = _throttle.Snapshot(), audit = _audit.GetSummary() });
}