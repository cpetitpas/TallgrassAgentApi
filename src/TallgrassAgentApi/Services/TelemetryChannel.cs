// Services/TelemetryChannel.cs
using System.Threading.Channels;
using TallgrassAgentApi.Models;

namespace TallgrassAgentApi.Services;

/// <summary>
/// Singleton unbounded channel that acts as the pipe between the
/// TelemetrySimulator producer and the SSE StreamController consumer.
/// </summary>
public class TelemetryChannel
{
    private readonly Channel<TelemetryEvent> _channel =
        Channel.CreateUnbounded<TelemetryEvent>(new UnboundedChannelOptions
        {
            SingleWriter = false,  // writer is publicly exposed and may be used by multiple publishers
            SingleReader = false   // multiple SSE clients may read concurrently
        });

    public ChannelWriter<TelemetryEvent> Writer => _channel.Writer;
    public ChannelReader<TelemetryEvent> Reader => _channel.Reader;
}
