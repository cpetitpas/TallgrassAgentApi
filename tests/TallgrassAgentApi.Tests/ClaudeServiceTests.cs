using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using TallgrassAgentApi.Models;
using TallgrassAgentApi.Services;
using Xunit;

namespace TallgrassAgentApi.Tests;

public class ClaudeServiceTests
{
    private static ClaudeService BuildService(string responseJson, AuditService audit)
    {
        var handler = new SingleResponseHandler(responseJson);
        var httpClient = new HttpClient(handler);
        var factory = new FakeHttpClientFactory(httpClient);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Anthropic:ApiKey"] = "test-key"
            })
            .Build();

        return new ClaudeService(factory, config, audit);
    }

    [Fact]
    public async Task AnalyzeAlarm_MissingUsage_DoesNotThrow_AndRecordsZeroTokens()
    {
        var responseJson = """
            {
              "content": [
                {
                  "type": "text",
                  "text": "{\"analysis\":\"stable\",\"recommended_action\":\"observe\",\"severity\":\"LOW\"}"
                }
              ]
            }
            """;

        var audit = new AuditService();
        var svc = BuildService(responseJson, audit);

        var raw = await svc.AnalyzeAlarmAsync(new AlarmRequest
        {
            NodeId = "NODE-003",
            AlarmType = "HIGH_PRESSURE",
            CurrentValue = 1290,
            Threshold = 1200,
            Unit = "PSI",
            Timestamp = DateTime.UtcNow
        });

        Assert.Contains("\"analysis\":\"stable\"", raw, StringComparison.Ordinal);

        var entry = audit.GetRecent(1).Single();
        Assert.Equal(AuditEntryKind.Alarm, entry.Kind);
        Assert.Equal("NODE-003", entry.NodeId);
        Assert.Equal(0, entry.InputTokens);
        Assert.Equal(0, entry.OutputTokens);
        Assert.Equal(200, entry.StatusCode);
    }

    [Fact]
    public async Task AnalyzeAlarm_NonTextFirstBlock_UsesFirstTextBlock_AndRecordsUsage()
    {
        var responseJson = """
            {
              "content": [
                {
                  "type": "tool_use",
                  "id": "tu_001",
                  "name": "noop",
                  "input": {}
                },
                {
                  "type": "text",
                  "text": "{\"analysis\":\"variance expected\",\"recommended_action\":\"continue monitoring\",\"severity\":\"MEDIUM\"}"
                }
              ],
              "usage": {
                "input_tokens": 123,
                "output_tokens": 45
              }
            }
            """;

        var audit = new AuditService();
        var svc = BuildService(responseJson, audit);

        var raw = await svc.AnalyzeAlarmAsync(new AlarmRequest
        {
            NodeId = "NODE-005",
            AlarmType = "FLOW_ANOMALY",
            CurrentValue = 110,
            Threshold = 100,
            Unit = "MMSCFD",
            Timestamp = DateTime.UtcNow
        });

        Assert.Contains("\"severity\":\"MEDIUM\"", raw, StringComparison.Ordinal);

        var entry = audit.GetRecent(1).Single();
        Assert.Equal(AuditEntryKind.Alarm, entry.Kind);
        Assert.Equal("NODE-005", entry.NodeId);
        Assert.Equal(123, entry.InputTokens);
        Assert.Equal(45, entry.OutputTokens);
        Assert.Equal(200, entry.StatusCode);
    }

    private sealed class FakeHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class SingleResponseHandler(string jsonBody) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(jsonBody, Encoding.UTF8, "application/json")
            };

            return Task.FromResult(response);
        }
    }
}
