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
if (builder.Environment.IsDevelopment())
    builder.Services.AddScoped<INodeClient, SimulatedNodeClient>();
else
    builder.Services.AddScoped<INodeClient, HttpNodeClient>();

// Swagger gives you a browser UI to test your endpoints
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var otlpEndpoint = builder.Configuration["Otel:OtlpEndpoint"] ?? "http://localhost:4317";
var resourceBuilder = ResourceBuilder.CreateDefault().AddService(TallgrassTelemetry.ServiceName);

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
        }));

// Auto-launch Aspire Dashboard in Development (no Docker required)
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddAspireDashboardInstaller();
    builder.Services.AddAspireDashboard();
}

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();
app.UseStaticFiles();
app.MapControllers();
app.Run();

public partial class Program { }