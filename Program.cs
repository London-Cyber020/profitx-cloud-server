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
Console.WriteLine("  ProfitX Cloud Trading Server v3.1");
Console.WriteLine("  Developer: London Cyber 2026");
Console.WriteLine("  Multi-User Cloud Trading Platform");
Console.WriteLine("  Running on port: " + port);
Console.WriteLine("═══════════════════════════════════════════════════════");

app.Run($"http://0.0.0.0:{port}");

// ═══════════════════════════════════════════════════════════════
// DATA STORE
// ═══════════════════════════════════════════════════════════════
public class DataStore
{
    // In-memory store (fast access)
    public ConcurrentDictionary<string, BotSession> ActiveBots { get; } = new();
    public ConcurrentDictionary<string, AccountData> AccountsData { get; } = new();
    public ConcurrentDictionary<string, List<TradeData>> OpenTrades { get; } = new();
    public ConcurrentDictionary<string, List<TradeData>> TradeHistory { get; } = new();
    public ConcurrentDictionary<string, UserMT5Connection> UserConnections { get; } = new();
    public ConcurrentDictionary<string, WorkerHeartbeat> WorkerHeartbeats { get; } = new();

    // Track last seen time for each worker
    public void UpdateWorkerHeartbeat(string key)
    {
        WorkerHeartbeats[key] = new WorkerHeartbeat
        {
            Key      = key,
            LastSeen = DateTime.UtcNow
        };
    }

    public bool IsWorkerOnline(string key)
    {
        if (!WorkerHeartbeats.TryGetValue(key, out var hb))
            return false;

        return (DateTime.UtcNow - hb.LastSeen).TotalSeconds < 30;
    }
}

// ═══════════════════════════════════════════════════════════════
// WORKER HEARTBEAT
// ═══════════════════════════════════════════════════════════════
public class WorkerHeartbeat
{
    public string Key { get; set; } = "";
    public DateTime LastSeen { get; set; }
    public bool IsOnline => (DateTime.UtcNow - LastSeen).TotalSeconds < 30;
}

// ═══════════════════════════════════════════════════════════════
// BOT SESSION
// ═══════════════════════════════════════════════════════════════
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
    public string Status { get; set; } = "STOPPED";
    public double MaxDailyLoss { get; set; } = 5.0;
    public double MaxDrawdown { get; set; } = 10.0;
    public double DailyStartBalance { get; set; }
    public double DailyProfit { get; set; }
    public int TotalTrades { get; set; }
    public int WinningTrades { get; set; }
    public int LosingTrades { get; set; }
}

// ═══════════════════════════════════════════════════════════════
// ACCOUNT DATA
// ═══════════════════════════════════════════════════════════════
public class AccountData
{
    public string AccountNumber { get; set; } = "";
    public string AccountName { get; set; } = "";
    public string Server { get; set; } = "";
    public string Currency { get; set; } = "USD";
    public int Leverage { get; set; }
    public double Balance { get; set; }
    public double Equity { get; set; }
    public double Margin { get; set; }
    public double FreeMargin { get; set; }
    public double MarginLevel { get; set; }
    public double Drawdown { get; set; }
    public double ProfitToday { get; set; }
    public double ProfitThisWeek { get; set; }
    public double ProfitThisMonth { get; set; }
    public int TotalTrades { get; set; }
    public int WinningTrades { get; set; }
    public int LosingTrades { get; set; }
    public int OpenTrades { get; set; }
    public bool IsConnected { get; set; }
    public string LastUpdate { get; set; } = "";
}

// ═══════════════════════════════════════════════════════════════
// TRADE DATA
// ═══════════════════════════════════════════════════════════════
public class TradeData
{
    public long Ticket { get; set; }
    public string Type { get; set; } = "";
    public string Symbol { get; set; } = "";
    public double LotSize { get; set; }
    public double EntryPrice { get; set; }
    public double CurrentPrice { get; set; }
    public double StopLoss { get; set; }
    public double TakeProfit { get; set; }
    public double Profit { get; set; }
    public double Swap { get; set; }
    public string OpenTime { get; set; } = "";
    public string CloseTime { get; set; } = "";
    public bool IsOpen { get; set; }
}

// ═══════════════════════════════════════════════════════════════
// USER MT5 CONNECTION
// ═══════════════════════════════════════════════════════════════
public class UserMT5Connection
{
    public string UserId { get; set; } = "";
    public string Mt5Login { get; set; } = "";
    // NOTE: Password intentionally NOT stored here
    // Worker holds credentials locally
    public string Mt5Server { get; set; } = "";
    public bool IsConnected { get; set; }
    public DateTime ConnectedAt { get; set; }
}

// ═══════════════════════════════════════════════════════════════
// CANDLE DATA
// ═══════════════════════════════════════════════════════════════
public class CandleData
{
    public string Time { get; set; } = "";
    public double Open { get; set; }
    public double High { get; set; }
    public double Low { get; set; }
    public double Close { get; set; }
    public long Volume { get; set; }
}
