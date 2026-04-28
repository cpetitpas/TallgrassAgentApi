using TallgrassAgentApi.Services;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using AspireRunner.AspNetCore;
using AspireRunner.Installer;
using TallgrassAgentApi.Telemetry;

var builder = WebApplication.CreateBuilder(args);

// Register services so they can be injected anywhere that needs them
builder.Services.AddControllers();
builder.Services.AddHttpClient();  // gives us HttpClient to call external APIs
builder.Services.AddScoped<IClaudeService, ClaudeService>();
builder.Services.AddHttpClient<IInvestigateService, InvestigateService>();  // service for investigation workflows
builder.Services.AddHttpClient<IChatService, ChatService>();
builder.Services.AddSingleton<ClaudeThrottle>();
builder.Services.AddSingleton<IAuditService, AuditService>();
builder.Services.AddSingleton<IConversationStore, InMemoryConversationStore>();
builder.Services.AddHttpClient<IMultiNodeInvestigateService, MultiNodeInvestigateService>();

// --- Telemetry streaming ---
builder.Services.AddSingleton<TelemetryChannel>();
builder.Services.AddHostedService<TelemetrySimulator>();

// --- Node health ---
builder.Services.AddSingleton<NodeHealthRegistry>();
builder.Services.AddHostedService<NodeHealthSweep>();

// INodeClient: swap SimulatedNodeClient for HttpNodeClient in production
if (builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing"))
    builder.Services.AddScoped<INodeClient, SimulatedNodeClient>();
else
    builder.Services.AddScoped<INodeClient, HttpNodeClient>();

// Swagger gives you a browser UI to test your endpoints
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var otlpEndpoint = builder.Configuration["Otel:OtlpEndpoint"] ?? "http://localhost:4317";
var otlpApiKey = builder.Configuration["AspireDashboard:Otlp:PrimaryApiKey"];
var resourceBuilder = ResourceBuilder.CreateDefault().AddService(TallgrassTelemetry.ServiceName);

if (builder.Environment.IsDevelopment())
{
    var frontendAuthMode = builder.Configuration["AspireDashboard:Frontend:AuthMode"];
    var frontendBrowserToken = builder.Configuration["AspireDashboard:Frontend:BrowserToken"];
    var otlpAuthMode = builder.Configuration["AspireDashboard:Otlp:AuthMode"];
    var mcpAuthMode = builder.Configuration["AspireDashboard:Mcp:AuthMode"];
    var mcpPrimaryApiKey = builder.Configuration["AspireDashboard:Mcp:PrimaryApiKey"];

    var missingEnvVars = new List<string>();

    if (string.Equals(frontendAuthMode, "BrowserToken", StringComparison.OrdinalIgnoreCase) &&
        string.IsNullOrWhiteSpace(frontendBrowserToken))
    {
        missingEnvVars.Add("AspireDashboard__Frontend__BrowserToken");
    }

    if (string.Equals(otlpAuthMode, "ApiKey", StringComparison.OrdinalIgnoreCase) &&
        string.IsNullOrWhiteSpace(otlpApiKey))
    {
        missingEnvVars.Add("AspireDashboard__Otlp__PrimaryApiKey");
    }

    if (string.Equals(mcpAuthMode, "ApiKey", StringComparison.OrdinalIgnoreCase) &&
        string.IsNullOrWhiteSpace(mcpPrimaryApiKey))
    {
        missingEnvVars.Add("AspireDashboard__Mcp__PrimaryApiKey");
    }

    if (missingEnvVars.Count > 0)
    {
        throw new InvalidOperationException(
            "Aspire dashboard security is enabled but required secrets are missing. " +
            $"Set these environment variables: {string.Join(", ", missingEnvVars)}.");
    }
}

builder.Logging.AddOpenTelemetry(logging =>
{
    logging.IncludeFormattedMessage = true;
    logging.IncludeScopes = true;
    logging.ParseStateValues = true;
    logging.SetResourceBuilder(resourceBuilder);
    logging.AddOtlpExporter(o =>
    {
        o.Endpoint = new Uri(otlpEndpoint);
        o.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.Grpc;
        if (!string.IsNullOrWhiteSpace(otlpApiKey))
            o.Headers = $"x-otlp-api-key={otlpApiKey}";
    });
});

// OpenTelemetry
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService(TallgrassTelemetry.ServiceName))
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddRuntimeInstrumentation()
        .AddOtlpExporter(o =>
        {
            o.Endpoint = new Uri(otlpEndpoint);
            o.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.Grpc;
            if (!string.IsNullOrWhiteSpace(otlpApiKey))
                o.Headers = $"x-otlp-api-key={otlpApiKey}";
        }))
    .WithTracing(tracing => tracing
        .AddSource(TallgrassTelemetry.Claude.Name)
        .AddSource(TallgrassTelemetry.Investigate.Name)
        .AddSource(TallgrassTelemetry.Chat.Name)
        .AddSource(TallgrassTelemetry.Node.Name)
        .AddAspNetCoreInstrumentation()   // auto-spans every HTTP request
        .AddHttpClientInstrumentation()   // auto-spans every outbound HttpClient call
        .AddOtlpExporter(o =>
        {
            o.Endpoint = new Uri(otlpEndpoint);
            o.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.Grpc;
            if (!string.IsNullOrWhiteSpace(otlpApiKey))
                o.Headers = $"x-otlp-api-key={otlpApiKey}";
        }));

// Auto-launch Aspire Dashboard in Development (no Docker required)
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddAspireDashboardInstaller();
    builder.Services.AddAspireDashboard(options =>
        builder.Configuration.GetSection("AspireDashboard").Bind(options));
}

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();
app.UseStaticFiles();
app.MapControllers();
app.Run();

public partial class Program { }