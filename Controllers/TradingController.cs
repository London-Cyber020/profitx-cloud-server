using Microsoft.AspNetCore.Mvc;

namespace ProfitX.CloudServer.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TradingController : ControllerBase
{
    private readonly DataStore _store;

    public TradingController(DataStore store)
    {
        _store = store;
    }

    // ── Connect MT5 ───────────────────────────────────────────
    [HttpPost("connectmt5")]
    public object ConnectMT5([FromBody] ConnectRequest request)
    {
        if (request == null)
            return new { success = false, message = "No data" };

        if (string.IsNullOrEmpty(request.Login) ||
            string.IsNullOrEmpty(request.Server))
            return new { success = false, message = "Login and Server required" };

        string key = $"{request.UserId}_{request.Login}";

        // NOTE: Password NOT stored in cloud - worker holds it locally
        _store.UserConnections[key] = new UserMT5Connection
        {
            UserId      = request.UserId,
            Mt5Login    = request.Login,
            Mt5Server   = request.Server,
            IsConnected = true,
            ConnectedAt = DateTime.UtcNow
        };

        Console.WriteLine($"MT5 credentials stored (no password): " +
                          $"{request.Login} on {request.Server}");

        return new
        {
            success = true,
            message = "MT5 credentials saved!\n\n" +
                      "Make sure Python Worker is running on your PC.",
            login  = request.Login,
            server = request.Server
        };
    }

    // ── Account Info ──────────────────────────────────────────
    [HttpGet("accountinfo")]
    public object AccountInfo(string userId = "", string mt5Login = "")
    {
        string key = $"{userId}_{mt5Login}";
        Console.WriteLine($"AccountInfo request: key={key}");

        AccountData? data = FindAccountData(userId, mt5Login, key);

        if (data != null && data.Balance > 0)
        {
            return new
            {
                success = true,
                account = new
                {
                    accountNumber  = data.AccountNumber,
                    accountName    = data.AccountName,
                    server         = data.Server,
                    currency       = data.Currency,
                    leverage       = data.Leverage,
                    balance        = data.Balance,
                    equity         = data.Equity,
                    margin         = data.Margin,
                    freeMargin     = data.FreeMargin,
                    marginLevel    = data.MarginLevel,
                    currentDrawdown = data.Drawdown,
                    profitToday    = data.ProfitToday,
                    profitThisWeek = data.ProfitThisWeek,
                    profitThisMonth = data.ProfitThisMonth,
                    totalTrades    = data.TotalTrades,
                    winningTrades  = data.WinningTrades,
                    losingTrades   = data.LosingTrades,
                    openTrades     = data.OpenTrades,
                    isConnected    = data.IsConnected,
                    lastUpdate     = data.LastUpdate
                }
            };
        }

        // Return connection info if worker registered but no data yet
        if (_store.UserConnections.ContainsKey(key))
        {
            var c = _store.UserConnections[key];
            bool workerOnline = _store.IsWorkerOnline(key);

            return new
            {
                success = true,
                account = new
                {
                    accountNumber   = c.Mt5Login,
                    accountName     = "",
                    server          = c.Mt5Server,
                    currency        = "USD",
                    leverage        = 0,
                    balance         = 0.0,
                    equity          = 0.0,
                    margin          = 0.0,
                    freeMargin      = 0.0,
                    marginLevel     = 0.0,
                    currentDrawdown = 0.0,
                    profitToday     = 0.0,
                    profitThisWeek  = 0.0,
                    profitThisMonth = 0.0,
                    totalTrades     = 0,
                    winningTrades   = 0,
                    losingTrades    = 0,
                    openTrades      = 0,
                    isConnected     = workerOnline,
                    lastUpdate      = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")
                }
            };
        }

        return new
        {
            success = false,
            message = "MT5 not connected. Start the Python Worker on your PC.",
            account = BuildEmptyAccount()
        };
    }

    // ── Start Bot ─────────────────────────────────────────────
    [HttpPost("startbot")]
    public object StartBot([FromBody] StartBotRequest request)
    {
        if (request == null)
            return new { success = false, message = "No data" };

        string key = $"{request.UserId}_{request.Mt5Login}";

        // Check if worker is online
        if (!_store.IsWorkerOnline(key))
        {
            return new
            {
                success = false,
                message = "⚠️ Python Worker is offline!\n\n" +
                          "Please start the worker on your PC first, " +
                          "then try starting the bot again."
            };
        }

        _store.ActiveBots[key] = new BotSession
        {
            UserId      = request.UserId,
            Mt5Login    = request.Mt5Login,
            Symbol      = request.Symbol,
            LotSize     = request.LotSize,
            Strategy    = request.Strategy,
            MaxTrades   = request.MaxTrades,
            MaxDailyLoss = request.MaxDailyLoss,
            MaxDrawdown  = request.MaxDrawdown,
            IsRunning   = true,
            StartTime   = DateTime.UtcNow,
            Status      = "Running"
        };

        Console.WriteLine($"BOT STARTED: {request.Symbol} " +
                          $"{request.Strategy} Lot:{request.LotSize} " +
                          $"MaxDL:{request.MaxDailyLoss}% MaxDD:{request.MaxDrawdown}%");

        return new
        {
            success  = true,
            message  = $"Bot started!\n\nSymbol: {request.Symbol}\n" +
                       $"Strategy: {request.Strategy}",
            symbol   = request.Symbol,
            strategy = request.Strategy,
            lotSize  = request.LotSize
        };
    }

    // ── Stop Bot ──────────────────────────────────────────────
    [HttpPost("stopbot")]
    public object StopBot([FromBody] StopBotRequest request)
    {
        if (request == null)
            return new { success = false, message = "No data" };

        string key = $"{request.UserId}_{request.Mt5Login}";

        if (_store.ActiveBots.ContainsKey(key))
        {
            _store.ActiveBots[key].IsRunning = false;
            _store.ActiveBots[key].Status    = "Stopped";
        }

        Console.WriteLine($"BOT STOPPED: key={key}");
        return new { success = true, message = "Bot stopped!" };
    }

    // ── Bot Status ────────────────────────────────────────────
    [HttpGet("botstatus")]
    public object BotStatus(string userId = "", string mt5Login = "")
    {
        string key = $"{userId}_{mt5Login}";

        bool workerOnline = _store.IsWorkerOnline(key);

        if (_store.ActiveBots.ContainsKey(key))
        {
            var bot = _store.ActiveBots[key];
            var rt  = DateTime.UtcNow - bot.StartTime;

            return new
            {
                success      = true,
                isRunning    = bot.IsRunning,
                workerOnline = workerOnline,
                symbol       = bot.Symbol,
                strategy     = bot.Strategy,
                lotSize      = bot.LotSize,
                maxTrades    = bot.MaxTrades,
                status       = bot.Status,
                runningTime  = bot.IsRunning
                    ? $"{(int)rt.TotalHours:D2}:{rt.Minutes:D2}:{rt.Seconds:D2}"
                    : "00:00:00"
            };
        }

        return new
        {
            success      = true,
            isRunning    = false,
            workerOnline = workerOnline,
            status       = "Not started"
        };
    }

    // ── Worker Status ─────────────────────────────────────────
    [HttpGet("workerstatus")]
    public object WorkerStatus(string userId = "", string mt5Login = "")
    {
        string key = $"{userId}_{mt5Login}";

        if (_store.WorkerHeartbeats.TryGetValue(key, out var hb))
        {
            return new
            {
                success  = true,
                isOnline = hb.IsOnline,
                lastSeen = hb.LastSeen.ToString("HH:mm:ss"),
                secondsAgo = (int)(DateTime.UtcNow - hb.LastSeen).TotalSeconds
            };
        }

        return new
        {
            success  = true,
            isOnline = false,
            lastSeen = "Never",
            secondsAgo = -1
        };
    }

    // ── Open Trades ───────────────────────────────────────────
    [HttpGet("opentrades")]
    public object OpenTrades(string userId = "", string mt5Login = "")
    {
        string key = $"{userId}_{mt5Login}";

        List<TradeData>? trades = null;

        if (_store.OpenTrades.ContainsKey(key))
            trades = _store.OpenTrades[key];

        if (trades == null)
        {
            foreach (var kvp in _store.OpenTrades)
            {
                if (kvp.Key.Contains(mt5Login) || kvp.Key.Contains(userId))
                {
                    trades = kvp.Value;
                    break;
                }
            }
        }

        if (trades != null)
        {
            return new
            {
                success = true,
                trades  = trades.Select(x => new
                {
                    ticket       = x.Ticket,
                    type         = x.Type == "BUY" ? 0 : 1,
                    typeString   = x.Type,
                    symbol       = x.Symbol,
                    lotSize      = x.LotSize,
                    entryPrice   = x.EntryPrice,
                    currentPrice = x.CurrentPrice,
                    stopLoss     = x.StopLoss,
                    takeProfit   = x.TakeProfit,
                    profit       = x.Profit,
                    swap         = x.Swap,
                    commission   = 0,
                    openTime     = x.OpenTime,
                    isOpen       = x.IsOpen
                }),
                count = trades.Count
            };
        }

        return new { success = true, trades = new List<object>(), count = 0 };
    }

    // ── Trade History ─────────────────────────────────────────
    [HttpGet("history")]
    public object History(
        string userId = "",
        string mt5Login = "",
        int days = 30)
    {
        string key = $"{userId}_{mt5Login}";

        if (_store.TradeHistory.ContainsKey(key))
        {
            var allHistory = _store.TradeHistory[key];

            // Apply days filter
            var cutoff  = DateTime.UtcNow.AddDays(-days);
            var filtered = allHistory.Where(t =>
            {
                if (DateTime.TryParse(t.CloseTime, out var closeDate))
                    return closeDate >= cutoff;
                return true; // Include if can't parse date
            }).ToList();

            return new
            {
                success = true,
                trades  = filtered,
                count   = filtered.Count
            };
        }

        return new { success = true, trades = new List<object>(), count = 0 };
    }

    // ── Symbols ───────────────────────────────────────────────
    [HttpGet("symbols")]
    public object Symbols() => new
    {
        success = true,
        symbols = new[]
        {
            "EURUSD", "GBPUSD", "USDJPY", "USDCHF", "AUDUSD",
            "USDCAD", "NZDUSD", "EURGBP", "EURJPY", "GBPJPY",
            "XAUUSD", "XAGUSD", "BTCUSD", "ETHUSD",
            "US30", "US100", "US500", "USOIL"
        }
    };

    // ── Strategies ────────────────────────────────────────────
    [HttpGet("strategies")]
    public object Strategies() => new
    {
        success    = true,
        strategies = new object[]
        {
            new { name = "ICT", fullName = "Inner Circle Trader" },
            new { name = "SMC", fullName = "Smart Money Concepts" }
        }
    };

    // ── Debug (admin key protected) ───────────────────────────
    [HttpGet("debug")]
    public object Debug([FromQuery] string adminKey = "")
    {
        string expectedKey = Environment.GetEnvironmentVariable("ADMIN_DEBUG_KEY") ?? "";

        if (string.IsNullOrEmpty(expectedKey) || adminKey != expectedKey)
            return Unauthorized(new { success = false, message = "Unauthorized" });

        return new
        {
            accountsDataKeys    = _store.AccountsData.Keys.ToList(),
            accountsDataValues  = _store.AccountsData.Select(x => new
            {
                key       = x.Key,
                balance   = x.Value.Balance,
                connected = x.Value.IsConnected
            }).ToList(),
            userConnectionKeys  = _store.UserConnections.Keys.ToList(),
            activeBotsKeys      = _store.ActiveBots.Keys.ToList(),
            openTradesKeys      = _store.OpenTrades.Keys.ToList(),
            workerHeartbeats    = _store.WorkerHeartbeats.Select(x => new
            {
                key      = x.Key,
                isOnline = x.Value.IsOnline,
                lastSeen = x.Value.LastSeen.ToString("HH:mm:ss")
            }).ToList()
        };
    }

    // ── Private Helpers ───────────────────────────────────────
    private AccountData? FindAccountData(
        string userId,
        string mt5Login,
        string exactKey)
    {
        // Try exact key first
        if (_store.AccountsData.TryGetValue(exactKey, out var data) &&
            data.Balance > 0)
            return data;

        // Try by mt5Login
        foreach (var kvp in _store.AccountsData)
        {
            if (kvp.Key.Contains(mt5Login) && kvp.Value.Balance > 0)
                return kvp.Value;
        }

        // Try by userId
        foreach (var kvp in _store.AccountsData)
        {
            if (kvp.Key.Contains(userId) && kvp.Value.Balance > 0)
                return kvp.Value;
        }

        // Try any with balance
        foreach (var kvp in _store.AccountsData)
        {
            if (kvp.Value.Balance > 0)
                return kvp.Value;
        }

        return null;
    }

    private object BuildEmptyAccount() => new
    {
        accountNumber   = "",
        accountName     = "",
        server          = "",
        currency        = "USD",
        leverage        = 0,
        balance         = 0.0,
        equity          = 0.0,
        margin          = 0.0,
        freeMargin      = 0.0,
        marginLevel     = 0.0,
        currentDrawdown = 0.0,
        profitToday     = 0.0,
        profitThisWeek  = 0.0,
        profitThisMonth = 0.0,
        totalTrades     = 0,
        winningTrades   = 0,
        losingTrades    = 0,
        openTrades      = 0,
        isConnected     = false,
        lastUpdate      = ""
    };
}

// ── Request Models ────────────────────────────────────────────
public class ConnectRequest
{
    public string UserId { get; set; } = "";
    public string Login { get; set; } = "";
    public string Password { get; set; } = "";  // Received but not stored
    public string Server { get; set; } = "";
}

public class StartBotRequest
{
    public string UserId { get; set; } = "";
    public string Mt5Login { get; set; } = "";
    public string Symbol { get; set; } = "";
    public double LotSize { get; set; }
    public string Strategy { get; set; } = "";
    public int MaxTrades { get; set; }
    public double MaxDailyLoss { get; set; } = 5.0;
    public double MaxDrawdown { get; set; } = 10.0;
}

public class StopBotRequest
{
    public string UserId { get; set; } = "";
    public string Mt5Login { get; set; } = "";
}
