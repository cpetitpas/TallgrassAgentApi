using TallgrassAgentApi.Services;

var builder = WebApplication.CreateBuilder(args);

// Register services so they can be injected anywhere that needs them
builder.Services.AddControllers();
builder.Services.AddHttpClient();  // gives us HttpClient to call external APIs
builder.Services.AddScoped<IClaudeService, ClaudeService>();  // our custom Claude wrapper

// --- Telemetry streaming ---
builder.Services.AddSingleton<TelemetryChannel>();
builder.Services.AddHostedService<TelemetrySimulator>();

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