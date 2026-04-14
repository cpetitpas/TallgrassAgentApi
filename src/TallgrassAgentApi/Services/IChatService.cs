using TallgrassAgentApi.Models;

namespace TallgrassAgentApi.Services;

public interface IChatService
{
    Task<ChatResponse> SendAsync(
        string      incidentId,
        string      nodeId,
        ChatRequest request,
        CancellationToken cancellationToken = default);
}