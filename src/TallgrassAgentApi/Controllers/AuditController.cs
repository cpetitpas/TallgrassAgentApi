using Microsoft.AspNetCore.Mvc;
using TallgrassAgentApi.Models;
using TallgrassAgentApi.Services;

namespace TallgrassAgentApi.Controllers;

[ApiController]
[Route("api/audit")]
public class AuditController : ControllerBase
{
    private readonly IAuditService _audit;
    public AuditController(IAuditService audit) => _audit = audit;

    /// <summary>GET /api/audit?count=50 — most recent N entries, newest first</summary>
    [HttpGet]
    public ActionResult<IReadOnlyList<AuditEntry>> GetRecent([FromQuery] int count = 50)
        => Ok(_audit.GetRecent(count));

    /// <summary>GET /api/audit/summary</summary>
    [HttpGet("summary")]
    public ActionResult<AuditSummary> GetSummary()
        => Ok(_audit.GetSummary());
}