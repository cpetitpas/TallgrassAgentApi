using System.Diagnostics;

namespace TallgrassAgentApi.Telemetry;

public static class TallgrassTelemetry
{
    public const string ServiceName = "TallgrassAgentApi";

    public static readonly ActivitySource Claude =
        new("TallgrassAgentApi.Claude");

    public static readonly ActivitySource Investigate =
        new("TallgrassAgentApi.Investigate");

    public static readonly ActivitySource Chat =
        new("TallgrassAgentApi.Chat");

    public static readonly ActivitySource Node =
        new("TallgrassAgentApi.Node");
}