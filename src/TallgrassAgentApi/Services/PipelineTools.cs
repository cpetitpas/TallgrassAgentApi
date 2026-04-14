using System.Text.Json;
using TallgrassAgentApi.Models;

namespace TallgrassAgentApi.Services;

/// <summary>
/// Static knowledge base + tool implementations.
/// Each public static method maps to a Claude tool by the same snake_case name.
/// </summary>
public static class PipelineTools
{
    // ── Tool definitions sent to Claude ────────────────────────────────────

    public static readonly IReadOnlyList<object> Definitions = new List<object>
    {
        new {
            name = "get_node_spec",
            description = "Returns static specification for a pipeline node: max pressure, pipe diameter, material, age, and rated flow range.",
            input_schema = new {
                type = "object",
                properties = new {
                    node_id = new { type = "string", description = "The node identifier, e.g. NODE-001" }
                },
                required = new[] { "node_id" }
            }
        },
        new {
            name = "get_recent_telemetry",
            description = "Returns the last N telemetry readings for a node (pressure, flow, temperature). Use to identify trends before an alarm.",
            input_schema = new {
                type = "object",
                properties = new {
                    node_id  = new { type = "string" },
                    count    = new { type = "integer", description = "Number of recent readings to return (max 20)", @default = 5 }
                },
                required = new[] { "node_id" }
            }
        },
        new {
            name = "get_maintenance_history",
            description = "Returns the last three maintenance events for a node: date, type, technician, and notes.",
            input_schema = new {
                type = "object",
                properties = new {
                    node_id = new { type = "string" }
                },
                required = new[] { "node_id" }
            }
        },
        new {
            name = "get_adjacent_nodes",
            description = "Returns the node IDs immediately upstream and downstream of the given node, plus their current health state.",
            input_schema = new {
                type = "object",
                properties = new {
                    node_id = new { type = "string" }
                },
                required = new[] { "node_id" }
            }
        },
        new {
            name = "get_pressure_thresholds",
            description = "Returns the warning and critical pressure thresholds configured for a node.",
            input_schema = new {
                type = "object",
                properties = new {
                    node_id = new { type = "string" }
                },
                required = new[] { "node_id" }
            }
        }
    };

    // ── Dispatch ────────────────────────────────────────────────────────────

    public static string Execute(string toolName, JsonElement input)
    {
        var nodeId = input.TryGetProperty("node_id", out var n) ? n.GetString() ?? "" : "";
        var count  = input.TryGetProperty("count",   out var c) ? c.GetInt32() : 5;

        return toolName switch
        {
            "get_node_spec"           => GetNodeSpec(nodeId),
            "get_recent_telemetry"    => GetRecentTelemetry(nodeId, count),
            "get_maintenance_history" => GetMaintenanceHistory(nodeId),
            "get_adjacent_nodes"      => GetAdjacentNodes(nodeId),
            "get_pressure_thresholds" => GetPressureThresholds(nodeId),
            _                         => """{"error":"unknown tool"}"""
        };
    }

    // ── Tool implementations (deterministic fake data) ──────────────────────

    private static string GetNodeSpec(string nodeId)
    {
        var specs = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["NODE-001"] = new { max_pressure_psi = 1440, pipe_diameter_in = 36, material = "X70 steel", age_years = 12, rated_flow_mmcfd = 850 },
            ["NODE-002"] = new { max_pressure_psi = 1440, pipe_diameter_in = 36, material = "X70 steel", age_years = 12, rated_flow_mmcfd = 850 },
            ["NODE-003"] = new { max_pressure_psi = 1200, pipe_diameter_in = 24, material = "X65 steel", age_years = 18, rated_flow_mmcfd = 420 },
            ["NODE-004"] = new { max_pressure_psi = 1200, pipe_diameter_in = 24, material = "X65 steel", age_years = 18, rated_flow_mmcfd = 420 },
            ["NODE-005"] = new { max_pressure_psi = 1440, pipe_diameter_in = 30, material = "X70 steel", age_years =  8, rated_flow_mmcfd = 650 },
        };

        if (specs.TryGetValue(nodeId, out var spec))
            return JsonSerializer.Serialize(spec);

        // Generic fallback for NODE-006 through NODE-010
        return JsonSerializer.Serialize(new { max_pressure_psi = 1200, pipe_diameter_in = 24, material = "X65 steel", age_years = 10, rated_flow_mmcfd = 400 });
    }

    private static string GetRecentTelemetry(string nodeId, int count)
    {
        count = Math.Clamp(count, 1, 20);
        var rng      = new Random(nodeId.GetHashCode());
        var readings = new List<object>();
        var now      = DateTimeOffset.UtcNow;

        for (int i = count; i >= 1; i--)
        {
            readings.Add(new
            {
                timestamp    = now.AddMinutes(-i * 3).ToString("o"),
                pressure_psi = Math.Round(980 + rng.NextDouble() * 80, 1),
                flow_mmcfd   = Math.Round(400 + rng.NextDouble() * 60, 2),
                temp_f       = Math.Round(58  + rng.NextDouble() * 10, 1)
            });
        }

        return JsonSerializer.Serialize(new { node_id = nodeId, readings });
    }

    private static string GetMaintenanceHistory(string nodeId)
    {
        var events = new[]
        {
            new { date = "2025-11-14", type = "Inspection",       technician = "R. Vasquez", notes = "No anomalies. Coating intact."          },
            new { date = "2025-06-02", type = "Valve Replacement", technician = "T. Holbrook", notes = "Ball valve seat replaced. Tested to 1200 PSI." },
            new { date = "2024-12-19", type = "Pig Run",           technician = "R. Vasquez", notes = "Minor internal corrosion noted at 2.1 miles from station. Monitoring." }
        };
        return JsonSerializer.Serialize(new { node_id = nodeId, history = events });
    }

    private static string GetAdjacentNodes(string nodeId)
    {
        // Simple linear topology: 001→002→003→004→005→...→010
        var num = int.TryParse(nodeId.Replace("NODE-", ""), out var n) ? n : 1;
        var upstream   = num > 1  ? $"NODE-{(num - 1):D3}" : null;
        var downstream = num < 10 ? $"NODE-{(num + 1):D3}" : null;

        return JsonSerializer.Serialize(new
        {
            node_id    = nodeId,
            upstream   = upstream   is null ? null : new { node_id = upstream,   health = "HEALTHY" },
            downstream = downstream is null ? null : new { node_id = downstream, health = "HEALTHY" }
        });
    }

    private static string GetPressureThresholds(string nodeId)
    {
        return JsonSerializer.Serialize(new
        {
            node_id          = nodeId,
            warning_psi      = 1100,
            critical_psi     = 1300,
            max_allowable    = 1440,
            hysteresis_psi   = 50
        });
    }
}