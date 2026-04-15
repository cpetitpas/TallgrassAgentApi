using Microsoft.AspNetCore.Mvc;
using TallgrassAgentApi.Models;
using TallgrassAgentApi.Services;

namespace TallgrassAgentApi.Controllers;

[ApiController]
[Route("api/incidents")]
public class IncidentChatController : ControllerBase
{
    private readonly IChatService         _chat;
    private readonly IConversationStore   _store;

    public IncidentChatController(IChatService chat, IConversationStore store)
    {
        _chat  = chat;
        _store = store;
    }

    /// <summary>POST /api/incidents/{incidentId}/chat?nodeId=NODE-003</summary>
    [HttpPost("{incidentId}/chat")]
    public async Task<ActionResult<ChatResponse>> Chat(
        string      incidentId,
        [FromQuery] string      nodeId,
        [FromBody]  ChatRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(incidentId))
            return BadRequest("incidentId is required.");
        if (string.IsNullOrWhiteSpace(nodeId))
            return BadRequest("nodeId query parameter is required.");
        if (string.IsNullOrWhiteSpace(request.Message))
            return BadRequest("Message is required.");

        var existing = _store.Get(incidentId);
        if (existing is not null &&
            !string.IsNullOrWhiteSpace(existing.NodeId) &&
            !string.Equals(existing.NodeId, nodeId, StringComparison.OrdinalIgnoreCase))
        {
            return Conflict($"Incident '{incidentId}' is already bound to node '{existing.NodeId}'.");
        }

        try
        {
            var response = await _chat.SendAsync(incidentId, nodeId, request, cancellationToken);
            return Ok(response);
        }
        catch (HttpRequestException ex)
        {
            return StatusCode(502, ex.Message);
        }
    }

    /// <summary>GET /api/incidents/{incidentId}/chat — full history</summary>
    [HttpGet("{incidentId}/chat")]
    public ActionResult<ConversationState> GetHistory(string incidentId)
    {
        var state = _store.Get(incidentId);
        if (state is null) return NotFound();
        return Ok(state);
    }

    /// <summary>GET /api/incidents — all active conversations</summary>
    [HttpGet]
    public ActionResult<IReadOnlyList<ConversationState>> GetAll()
        => Ok(_store.All());

    /// <summary>DELETE /api/incidents/{incidentId} — clear conversation</summary>
    [HttpDelete("{incidentId}")]
    public IActionResult Delete(string incidentId)
        => _store.Delete(incidentId) ? NoContent() : NotFound();
}