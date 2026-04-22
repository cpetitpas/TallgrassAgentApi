using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging;

namespace TallgrassAgentApi.Tests;

internal static class TestWebHostFactory
{
    public static WebApplicationFactory<Program> CreateQuietFactory(
        Action<IWebHostBuilder>? configureBuilder = null)
        => new WebApplicationFactory<Program>().WithQuietHost(configureBuilder);

    public static WebApplicationFactory<Program> WithQuietHost(
        this WebApplicationFactory<Program> factory,
        Action<IWebHostBuilder>? configureBuilder = null)
    {
        return factory.WithWebHostBuilder(builder =>
        {
            // Silence host/framework logs in tests to keep output focused.
            builder.ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.SetMinimumLevel(LogLevel.None);
            });

            configureBuilder?.Invoke(builder);
        });
    }
}
