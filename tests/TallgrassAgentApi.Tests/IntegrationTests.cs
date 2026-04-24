using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using TallgrassAgentApi.Models;
using TallgrassAgentApi.Services;
using Xunit;

namespace TallgrassAgentApi.Tests;

// Disable test parallelization for integration tests to reduce API load
[CollectionDefinition("Integration Tests", DisableParallelization = true)]
public class IntegrationTestCollection { }

public sealed class IntegrationFactAttribute : FactAttribute
{
    public const string EnableEnvVar = "RUN_INTEGRATION_TESTS";
    public const string ApiKeyEnvVar = "Anthropic__ApiKey";

    public IntegrationFactAttribute()
    {
        var enabled = Environment.GetEnvironmentVariable(EnableEnvVar);
        if (!string.Equals(enabled, "true", StringComparison.OrdinalIgnoreCase))
        {
            Skip = $"Integration tests are skipped by default. Set {EnableEnvVar}=true to enable.";
            return;
        }

        var apiKey = Environment.GetEnvironmentVariable(ApiKeyEnvVar);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            Skip = $"Integration tests require {ApiKeyEnvVar} environment variable.";
        }
    }
}

// Note: IntegrationTests does not inherit TestBase because it uses the real
// ClaudeService with a live API key rather than the FakeClaudeService.
// Helper methods are duplicated here intentionally.
[Collection("Integration Tests")]
[Trait("Category", "Integration")]
public class IntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;
    private static Task<MultiNodeResponse>? s_cachedMultiNodeResponse;
    private static readonly HashSet<HttpStatusCode> TransientStatuses =
    [
        HttpStatusCode.TooManyRequests,
        HttpStatusCode.BadGateway,
        HttpStatusCode.ServiceUnavailable,
        HttpStatusCode.GatewayTimeout
    ];

    public IntegrationTests(WebApplicationFactory<Program> factory)
    {
        // Load API key from environment variable for tests
        _client = factory.WithQuietHost(builder =>
        {
            builder.ConfigureAppConfiguration((context, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Anthropic:ApiKey"] = Environment.GetEnvironmentVariable("Anthropic__ApiKey")
                        ?? throw new Exception("Anthropic__ApiKey environment variable not set")
                });
            });
        }).CreateClient();
    }

    // -------------------------
    // ALARM ENDPOINT TESTS
    // -------------------------

    [IntegrationFact]
    public async Task AlarmAnalyze_ReturnsSuccess()
    {
        var request = GetTestAlarmRequest();
        var response = await PostAsync("/api/alarm/analyze", request);
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }

    [IntegrationFact]
    public async Task AlarmAnalyze_NodeId_IsPopulated()
    {
        var request = GetTestAlarmRequest();
        var result = await PostAndDeserialize<AlarmResponse>("/api/alarm/analyze", request);
        Assert.False(string.IsNullOrWhiteSpace(result.NodeId), "NodeId should not be empty");
    }

    [IntegrationFact]
    public async Task AlarmAnalyze_Analysis_IsPopulated()
    {
        var request = GetTestAlarmRequest();
        var result = await PostAndDeserialize<AlarmResponse>("/api/alarm/analyze", request);
        Assert.False(string.IsNullOrWhiteSpace(result.Analysis), "Analysis should not be empty");
    }

    [IntegrationFact]
    public async Task AlarmAnalyze_RecommendedAction_IsPopulated()
    {
        var request = GetTestAlarmRequest();
        var result = await PostAndDeserialize<AlarmResponse>("/api/alarm/analyze", request);
        Assert.False(string.IsNullOrWhiteSpace(result.RecommendedAction), "RecommendedAction should not be empty");
    }

    [IntegrationFact]
    public async Task AlarmAnalyze_Severity_IsValidValue()
    {
        var request = GetTestAlarmRequest();
        var result = await PostAndDeserialize<AlarmResponse>("/api/alarm/analyze", request);
        var valid = new[] { "LOW", "MEDIUM", "HIGH" };
        Assert.Contains(result.Severity, valid);
    }

    // -------------------------
    // FLOW ENDPOINT TESTS
    // -------------------------

    [IntegrationFact]
    public async Task FlowAnalyze_ReturnsSuccess()
    {
        var request = GetTestFlowRequest();
        var response = await PostAsync("/api/flow/analyze", request);
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }

    [IntegrationFact]
    public async Task FlowAnalyze_NodeId_IsPopulated()
    {
        var request = GetTestFlowRequest();
        var result = await PostAndDeserialize<FlowResponse>("/api/flow/analyze", request);
        Assert.False(string.IsNullOrWhiteSpace(result.NodeId), "NodeId should not be empty");
    }

    [IntegrationFact]
    public async Task FlowAnalyze_PipelineSegment_IsPopulated()
    {
        var request = GetTestFlowRequest();
        var result = await PostAndDeserialize<FlowResponse>("/api/flow/analyze", request);
        Assert.False(string.IsNullOrWhiteSpace(result.PipelineSegment), "PipelineSegment should not be empty");
    }

    [IntegrationFact]
    public async Task FlowAnalyze_Analysis_IsPopulated()
    {
        var request = GetTestFlowRequest();
        var result = await PostAndDeserialize<FlowResponse>("/api/flow/analyze", request);
        Assert.False(string.IsNullOrWhiteSpace(result.Analysis), "Analysis should not be empty");
    }

    [IntegrationFact]
    public async Task FlowAnalyze_RecommendedAction_IsPopulated()
    {
        var request = GetTestFlowRequest();
        var result = await PostAndDeserialize<FlowResponse>("/api/flow/analyze", request);
        Assert.False(string.IsNullOrWhiteSpace(result.RecommendedAction), "RecommendedAction should not be empty");
    }

    [IntegrationFact]
    public async Task FlowAnalyze_Severity_IsValidValue()
    {
        var request = GetTestFlowRequest();
        var result = await PostAndDeserialize<FlowResponse>("/api/flow/analyze", request);
        var valid = new[] { "LOW", "MEDIUM", "HIGH" };
        Assert.Contains(result.Severity, valid);
    }

    [IntegrationFact]
    public async Task FlowAnalyze_Variance_IsCalculated()
    {
        var request = GetTestFlowRequest();
        var result = await PostAndDeserialize<FlowResponse>("/api/flow/analyze", request);
        Assert.NotEqual(0.0, result.Variance);
    }

    // -------------------------
    // MULTI-NODE ENDPOINT TESTS
    // -------------------------

    [IntegrationFact]
    public async Task MultiNodeAnalyze_ReturnsSuccess()
    {
        var request = GetTestMultiNodeRequest();
        var response = await PostAsync("/api/multinode/analyze", request);
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }

    [IntegrationFact]
    public async Task MultiNodeAnalyze_RegionId_IsPopulated()
    {
        var result = await GetMultiNodeResultAsync();
        Assert.False(string.IsNullOrWhiteSpace(result.RegionId), "RegionId should not be empty");
    }

    [IntegrationFact]
    public async Task MultiNodeAnalyze_Summary_IsPopulated()
    {
        var result = await GetMultiNodeResultAsync();
        Assert.False(string.IsNullOrWhiteSpace(result.Summary), "Summary should not be empty");
    }

    [IntegrationFact]
    public async Task MultiNodeAnalyze_RecommendedAction_IsPopulated()
    {
        var result = await GetMultiNodeResultAsync();
        Assert.False(string.IsNullOrWhiteSpace(result.RecommendedAction), "RecommendedAction should not be empty");
    }

    [IntegrationFact]
    public async Task MultiNodeAnalyze_OverallStatus_IsValidValue()
    {
        var result = await GetMultiNodeResultAsync();
        var valid = new[] { "NORMAL", "DEGRADED", "CRITICAL" };
        Assert.Contains(result.OverallStatus, valid);
    }

    [IntegrationFact]
    public async Task MultiNodeAnalyze_NodeCounts_AddUpToTotal()
    {
        var result = await GetMultiNodeResultAsync();
        var countSum = result.CriticalCount + result.WarningCount + result.NormalCount;
        Assert.Equal(result.TotalNodes, countSum);
    }

    [IntegrationFact]
    public async Task MultiNodeAnalyze_AffectedNodes_IsNotNull()
    {
        var result = await GetMultiNodeResultAsync();
        Assert.NotNull(result.AffectedNodes);
    }

    [IntegrationFact]
    public async Task MultiNodeAnalyze_EmptyReadings_ReturnsBadRequest()
    {
        var request = new MultiNodeRequest { RegionId = "REGION-TEST", Readings = new() };
        var response = await PostAsync("/api/multinode/analyze", request);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
    }

    // -------------------------
    // HELPERS
    // -------------------------

    private async Task<HttpResponseMessage> PostAsync(string url, object body)
    {
        const int maxAttempts = 5;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var json = JsonSerializer.Serialize(body);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _client.PostAsync(url, content);

            if (!TransientStatuses.Contains(response.StatusCode) || attempt == maxAttempts)
            {
                return response;
            }

            response.Dispose();
            // Exponential backoff with jitter: 500ms, 1s, 2s, 4s
            var baseDelayMs = 500 * (int)Math.Pow(2, attempt - 1);
            var jitterMs = Random.Shared.Next(0, baseDelayMs / 2);
            var delayMs = baseDelayMs + jitterMs;
            await Task.Delay(delayMs);
        }

        throw new InvalidOperationException("Unreachable retry branch in PostAsync.");
    }

    private async Task<T> PostAndDeserialize<T>(string url, object body)
    {
        using var response = await PostAsync(url, body);
        // If still transient after retries, surface a clear upstream-failure message.
        if (TransientStatuses.Contains(response.StatusCode))
        {
            throw new InvalidOperationException($"Upstream API unreachable after retries: {response.StatusCode}");
        }

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<T>();
        Assert.NotNull(result);
        return result!;
    }

    private Task<MultiNodeResponse> GetMultiNodeResultAsync()
    {
        s_cachedMultiNodeResponse ??= PostAndDeserialize<MultiNodeResponse>(
            "/api/multinode/analyze",
            GetTestMultiNodeRequest());
        return s_cachedMultiNodeResponse;
    }

    private static AlarmRequest GetTestAlarmRequest() => new()
    {
        NodeId = "NODE-042",
        AlarmType = "HIGH_PRESSURE",
        CurrentValue = 847.3,
        Threshold = 800.0,
        Unit = "PSI",
        Timestamp = DateTime.UtcNow
    };

    private static FlowRequest GetTestFlowRequest() => new()
    {
        NodeId = "NODE-017",
        PipelineSegment = "SEG-7A",
        FlowRate = 118.5,
        ExpectedFlowRate = 150.0,
        Unit = "MMSCFD",
        FlowDirection = "FORWARD",
        Timestamp = DateTime.UtcNow
    };

    private static MultiNodeRequest GetTestMultiNodeRequest() => new()
    {
        RegionId = "REGION-WEST-4",
        Readings = new()
        {
            new() { NodeId = "NODE-011", ReadingType = "ALARM", MetricName = "PRESSURE",
                    CurrentValue = 798.0, ExpectedValue = 850.0, Unit = "PSI",
                    Status = "WARNING", Timestamp = DateTime.UtcNow },
            new() { NodeId = "NODE-012", ReadingType = "ALARM", MetricName = "PRESSURE",
                    CurrentValue = 741.0, ExpectedValue = 850.0, Unit = "PSI",
                    Status = "CRITICAL", Timestamp = DateTime.UtcNow },
            new() { NodeId = "NODE-013", ReadingType = "ALARM", MetricName = "PRESSURE",
                    CurrentValue = 685.0, ExpectedValue = 850.0, Unit = "PSI",
                    Status = "CRITICAL", Timestamp = DateTime.UtcNow },
            new() { NodeId = "NODE-014", ReadingType = "FLOW", MetricName = "FLOW_RATE",
                    CurrentValue = 98.0, ExpectedValue = 150.0, Unit = "MMSCFD",
                    Status = "WARNING", Timestamp = DateTime.UtcNow },
            new() { NodeId = "NODE-015", ReadingType = "FLOW", MetricName = "FLOW_RATE",
                    CurrentValue = 151.0, ExpectedValue = 150.0, Unit = "MMSCFD",
                    Status = "NORMAL", Timestamp = DateTime.UtcNow }
        }
    };
}