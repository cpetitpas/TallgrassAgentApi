using TallgrassAgentApi.Services;

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

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();
app.UseStaticFiles();
app.MapControllers();
app.Run();

public partial class Program { }