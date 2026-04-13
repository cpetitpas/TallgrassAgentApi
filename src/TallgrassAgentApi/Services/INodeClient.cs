// Services/INodeClient.cs
using TallgrassAgentApi.Models;

namespace TallgrassAgentApi.Services;

public interface INodeClient
{
    /// <summary>
    /// Actively ping a node and return its current health status.
    /// </summary>
    Task<NodePingResult> PingAsync(string nodeId, CancellationToken ct = default);
}
