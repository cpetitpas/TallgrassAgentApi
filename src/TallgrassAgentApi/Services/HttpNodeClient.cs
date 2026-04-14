// Services/HttpNodeClient.cs
using System.Net.Sockets;
using System.Text.Json;
using TallgrassAgentApi.Models;

namespace TallgrassAgentApi.Services;

/// <summary>
/// Production INodeClient. Each node is expected to expose a lightweight
/// HTTP health endpoint at GET http://{host}/health that returns a JSON
/// object with the fields below (all optional except status).
///
/// Node addresses are configured in appsettings.json under "NodeAddresses":
///   "NodeAddresses": {
///     "NODE-001": "http://192.168.10.1",
///     "NODE-002": "http://192.168.10.2"
///   }
///
/// Nodes not found in config are treated as OFFLINE.
/// </summary>
public class HttpNodeClient : INodeClient
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _config;
    private readonly ILogger<HttpNodeClient> _logger;

    public HttpNodeClient(
        IHttpClientFactory httpClientFactory,
        IConfiguration config,
        ILogger<HttpNodeClient> logger)
    {
        _httpClient = httpClientFactory.CreateClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(10);
        _config = config;
        _logger = logger;
    }

    public async Task<NodePingResult> PingAsync(string nodeId, CancellationToken ct = default)
    {
        var address = _config[$"NodeAddresses:{nodeId}"];
        if (string.IsNullOrEmpty(address))
        {
            _logger.LogWarning("No address configured for node {NodeId}", nodeId);
            return Offline(nodeId, "No address configured for this node.");
        }

        var url = $"{address.TrimEnd('/')}/health";
        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            var response = await _httpClient.GetAsync(url, ct);
            sw.Stop();

            if (!response.IsSuccessStatusCode)
            {
                return new NodePingResult
                {
                    NodeId       = nodeId,
                    Reachable    = true,
                    RoundTripMs  = (int)sw.ElapsedMilliseconds,
                    Status       = "DEGRADED",
                    ErrorMessage = $"Node returned HTTP {(int)response.StatusCode}"
                };
            }

            var body = await response.Content.ReadAsStringAsync(ct);
            return ParseHealthResponse(nodeId, body, (int)sw.ElapsedMilliseconds);
        }
        catch (TaskCanceledException)
        {
            sw.Stop();
            return Offline(nodeId, $"Ping timed out after {sw.ElapsedMilliseconds}ms");
        }
        catch (HttpRequestException ex) when (ex.InnerException is SocketException)
        {
            return Offline(nodeId, "Node unreachable — connection refused or network error.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error pinging node {NodeId}", nodeId);
            return Offline(nodeId, ex.Message);
        }
    }

    private static NodePingResult ParseHealthResponse(string nodeId, string body, int roundTripMs)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            return new NodePingResult
            {
                NodeId          = nodeId,
                Reachable       = true,
                RoundTripMs     = roundTripMs,
                Status          = root.TryGetProperty("status", out var s) ? s.GetString() ?? "ONLINE" : "ONLINE",
                FirmwareVersion = root.TryGetProperty("firmwareVersion", out var fw) ? fw.GetString() : null,
                SignalStrength  = root.TryGetProperty("signalStrength", out var sig) ? sig.GetDouble() : null,
                BatteryPercent  = root.TryGetProperty("batteryPercent", out var bat) ? bat.GetDouble() : null,
            };
        }
        catch
        {
            // Node responded but body wasn't parseable JSON — treat as alive
            return new NodePingResult
            {
                NodeId      = nodeId,
                Reachable   = true,
                RoundTripMs = roundTripMs,
                Status      = "ONLINE"
            };
        }
    }

    private static NodePingResult Offline(string nodeId, string reason) => new()
    {
        NodeId       = nodeId,
        Reachable    = false,
        Status       = "OFFLINE",
        ErrorMessage = reason
    };
}
