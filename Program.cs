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
builder.Services.AddSingleton<MetaApiService>();

var app = builder.Build();

app.UseCors("AllowAll");
app.MapControllers();

var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";

Console.WriteLine("═══════════════════════════════════════════════════════");
Console.WriteLine("  ProfitX Cloud Trading Server v2.0");
Console.WriteLine("  Developer: London Cyber 2026");
Console.WriteLine("  NO PC NEEDED - Pure Cloud Trading!");
Console.WriteLine("  Running on port: " + port);
Console.WriteLine("═══════════════════════════════════════════════════════");

app.Run($"http://0.0.0.0:{port}");

// ═══════════════════════════════════════════════════════════════
// DATA STORE
// ═══════════════════════════════════════════════════════════════
public class DataStore
{
    public ConcurrentDictionary<string, BotSession> ActiveBots { get; } = new();
    public ConcurrentDictionary<string, AccountData> AccountsData { get; } = new();
    public ConcurrentDictionary<string, List<TradeData>> OpenTrades { get; } = new();
    public ConcurrentDictionary<string, List<TradeData>> TradeHistory { get; } = new();
    public ConcurrentDictionary<string, MT5Credentials> MT5Accounts { get; } = new();
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
    public string Status { get; set; } = "STOPPED";
}

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
    public int OpenTrades { get; set; }
    public bool IsConnected { get; set; }
    public string LastUpdate { get; set; } = "";
}

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

public class MT5Credentials
{
    public string UserId { get; set; } = "";
    public string Login { get; set; } = "";
    public string Password { get; set; } = "";
    public string Server { get; set; } = "";
    public string Platform { get; set; } = "mt5";
    public bool IsConnected { get; set; }
}
