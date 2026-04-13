using System.Collections.Concurrent;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
    });
});

builder.Services.AddSingleton<DataStore>();

var app = builder.Build();

app.UseCors("AllowAll");
app.MapControllers();

var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";

Console.WriteLine("═══════════════════════════════════════════════════════");
Console.WriteLine("  ProfitX Cloud Server v1.0");
Console.WriteLine("  Developer: London Cyber 2026");
Console.WriteLine("  Running on port: " + port);
Console.WriteLine("═══════════════════════════════════════════════════════");

app.Run($"http://0.0.0.0:{port}");

public class DataStore
{
    public ConcurrentDictionary<string, Dictionary<string, string>> PcData { get; } = new();
    public ConcurrentDictionary<string, string> PendingCommands { get; } = new();
    public ConcurrentDictionary<string, object> RegisteredPCs { get; } = new();
    public ConcurrentDictionary<string, BotSession> ActiveBots { get; } = new();
}

public class BotSession
{
    public string UserId { get; set; } = "";
    public string Mt5Login { get; set; } = "";
    public string Symbol { get; set; } = "";
    public double LotSize { get; set; }
    public string Strategy { get; set; } = "";
    public int MaxTrades { get; set; }
    public bool IsRunning { get; set; }
    public DateTime StartTime { get; set; }
}
