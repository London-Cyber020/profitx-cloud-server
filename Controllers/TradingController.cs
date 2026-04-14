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

    [HttpPost("connectmt5")]
    public object ConnectMT5([FromBody] ConnectRequest request)
    {
        if (request == null) return new { success = false, message = "No data" };

        if (string.IsNullOrEmpty(request.Login) || string.IsNullOrEmpty(request.Password) || string.IsNullOrEmpty(request.Server))
            return new { success = false, message = "All MT5 fields required" };

        string key = $"{request.UserId}_{request.Login}";

        _store.UserConnections[key] = new UserMT5Connection
        {
            UserId = request.UserId,
            Mt5Login = request.Login,
            Mt5Password = request.Password,
            Mt5Server = request.Server,
            IsConnected = true,
            ConnectedAt = DateTime.Now
        };

        Console.WriteLine($"MT5 credentials stored: {request.Login} on {request.Server}");

        return new
        {
            success = true,
            message = "MT5 credentials saved!\n\nMake sure Python Worker is running on PC.",
            login = request.Login,
            server = request.Server
        };
    }

    [HttpGet("accountinfo")]
    public object AccountInfo(string userId = "", string mt5Login = "")
    {
        string key = $"{userId}_{mt5Login}";

        Console.WriteLine($"AccountInfo request: key={key}");

        // Check ALL possible keys for data
        AccountData? data = null;

        // Try exact key
        if (_store.AccountsData.ContainsKey(key))
        {
            data = _store.AccountsData[key];
            Console.WriteLine($"Found data with key: {key}");
        }

        // Try finding by mt5Login only
        if (data == null || data.Balance == 0)
        {
            foreach (var kvp in _store.AccountsData)
            {
                if (kvp.Key.Contains(mt5Login) && kvp.Value.Balance > 0)
                {
                    data = kvp.Value;
                    Console.WriteLine($"Found data with partial key: {kvp.Key}");
                    break;
                }
            }
        }

        // Try finding by userId only
        if (data == null || data.Balance == 0)
        {
            foreach (var kvp in _store.AccountsData)
            {
                if (kvp.Key.Contains(userId) && kvp.Value.Balance > 0)
                {
                    data = kvp.Value;
                    Console.WriteLine($"Found data with userId key: {kvp.Key}");
                    break;
                }
            }
        }

        // Try any data that has balance
        if (data == null || data.Balance == 0)
        {
            foreach (var kvp in _store.AccountsData)
            {
                if (kvp.Value.Balance > 0)
                {
                    data = kvp.Value;
                    Console.WriteLine($"Found data with any key: {kvp.Key}");
                    break;
                }
            }
        }

        if (data != null && data.Balance > 0)
        {
            return new
            {
                success = true,
                account = new
                {
                    accountNumber = data.AccountNumber,
                    accountName = data.AccountName,
                    server = data.Server,
                    currency = data.Currency,
                    leverage = data.Leverage,
                    balance = data.Balance,
                    equity = data.Equity,
                    margin = data.Margin,
                    freeMargin = data.FreeMargin,
                    marginLevel = data.MarginLevel,
                    currentDrawdown = data.Drawdown,
                    profitToday = data.ProfitToday,
                    profitThisWeek = 0.0,
                    profitThisMonth = 0.0,
                    totalTrades = 0,
                    winningTrades = 0,
                    losingTrades = 0,
                    openTrades = data.OpenTrades,
                    isConnected = data.IsConnected,
                    lastUpdate = data.LastUpdate
                }
            };
        }

        // Return connection info if exists but no balance data
        if (_store.UserConnections.ContainsKey(key))
        {
            var c = _store.UserConnections[key];
            return new
            {
                success = true,
                account = new
                {
                    accountNumber = c.Mt5Login,
                    accountName = "",
                    server = c.Mt5Server,
                    currency = "USD",
                    leverage = 0,
                    balance = 0.0,
                    equity = 0.0,
                    margin = 0.0,
                    freeMargin = 0.0,
                    marginLevel = 0.0,
                    currentDrawdown = 0.0,
                    profitToday = 0.0,
                    profitThisWeek = 0.0,
                    profitThisMonth = 0.0,
                    totalTrades = 0,
                    winningTrades = 0,
                    losingTrades = 0,
                    openTrades = 0,
                    isConnected = c.IsConnected,
                    lastUpdate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                }
            };
        }

        // Log all available keys for debugging
        Console.WriteLine($"Available AccountsData keys:");
        foreach (var k in _store.AccountsData.Keys)
            Console.WriteLine($"  - {k} (Balance: {_store.AccountsData[k].Balance})");

        Console.WriteLine($"Available UserConnections keys:");
        foreach (var k in _store.UserConnections.Keys)
            Console.WriteLine($"  - {k}");

        return new
        {
            success = false,
            message = "MT5 not connected",
            account = new
            {
                accountNumber = "", accountName = "", server = "",
                currency = "USD", leverage = 0,
                balance = 0.0, equity = 0.0, margin = 0.0, freeMargin = 0.0,
                marginLevel = 0.0, currentDrawdown = 0.0, profitToday = 0.0,
                profitThisWeek = 0.0, profitThisMonth = 0.0,
                totalTrades = 0, winningTrades = 0, losingTrades = 0,
                openTrades = 0, isConnected = false, lastUpdate = ""
            }
        };
    }

    [HttpPost("startbot")]
    public object StartBot([FromBody] StartBotRequest request)
    {
        if (request == null) return new { success = false, message = "No data" };

        string key = $"{request.UserId}_{request.Mt5Login}";

        _store.ActiveBots[key] = new BotSession
        {
            UserId = request.UserId,
            Mt5Login = request.Mt5Login,
            Symbol = request.Symbol,
            LotSize = request.LotSize,
            Strategy = request.Strategy,
            MaxTrades = request.MaxTrades,
            IsRunning = true,
            StartTime = DateTime.Now,
            Status = "Running"
        };

        Console.WriteLine($"BOT STARTED: {request.Symbol} {request.Strategy} Lot:{request.LotSize}");

        return new
        {
            success = true,
            message = $"Bot started!\n\nSymbol: {request.Symbol}\nStrategy: {request.Strategy}",
            symbol = request.Symbol,
            strategy = request.Strategy,
            lotSize = request.LotSize
        };
    }

    [HttpPost("stopbot")]
    public object StopBot([FromBody] StopBotRequest request)
    {
        if (request == null) return new { success = false, message = "No data" };

        string key = $"{request.UserId}_{request.Mt5Login}";

        if (_store.ActiveBots.ContainsKey(key))
        {
            _store.ActiveBots[key].IsRunning = false;
            _store.ActiveBots[key].Status = "Stopped";
        }

        return new { success = true, message = "Bot stopped!" };
    }

    [HttpGet("botstatus")]
    public object BotStatus(string userId = "", string mt5Login = "")
    {
        string key = $"{userId}_{mt5Login}";

        if (_store.ActiveBots.ContainsKey(key))
        {
            var bot = _store.ActiveBots[key];
            var rt = DateTime.Now - bot.StartTime;

            return new
            {
                success = true,
                isRunning = bot.IsRunning,
                symbol = bot.Symbol,
                strategy = bot.Strategy,
                lotSize = bot.LotSize,
                maxTrades = bot.MaxTrades,
                status = bot.Status,
                runningTime = bot.IsRunning ?
                    $"{rt.Hours:D2}:{rt.Minutes:D2}:{rt.Seconds:D2}" : "00:00:00"
            };
        }

        return new { success = true, isRunning = false, status = "Not started" };
    }

    [HttpGet("opentrades")]
    public object OpenTrades(string userId = "", string mt5Login = "")
    {
        string key = $"{userId}_{mt5Login}";

        // Search all keys
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
                trades = trades.Select(x => new
                {
                    ticket = x.Ticket,
                    type = x.Type == "BUY" ? 0 : 1,
                    typeString = x.Type,
                    symbol = x.Symbol,
                    lotSize = x.LotSize,
                    entryPrice = x.EntryPrice,
                    currentPrice = x.CurrentPrice,
                    stopLoss = x.StopLoss,
                    takeProfit = x.TakeProfit,
                    profit = x.Profit,
                    swap = x.Swap,
                    commission = 0,
                    openTime = x.OpenTime,
                    isOpen = x.IsOpen
                }),
                count = trades.Count
            };
        }

        return new { success = true, trades = new List<object>(), count = 0 };
    }

    [HttpGet("history")]
    public object History(string userId = "", string mt5Login = "", int days = 30)
    {
        string key = $"{userId}_{mt5Login}";

        if (_store.TradeHistory.ContainsKey(key))
            return new { success = true, trades = _store.TradeHistory[key], count = _store.TradeHistory[key].Count };

        return new { success = true, trades = new List<object>(), count = 0 };
    }

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

    [HttpGet("strategies")]
    public object Strategies() => new
    {
        success = true,
        strategies = new object[]
        {
            new { name = "ICT", fullName = "Inner Circle Trader" },
            new { name = "SMC", fullName = "Smart Money Concepts" }
        }
    };

    // DEBUG: Show all stored data keys
    [HttpGet("debug")]
    public object Debug()
    {
        return new
        {
            accountsDataKeys = _store.AccountsData.Keys.ToList(),
            accountsDataValues = _store.AccountsData.Select(x => new { key = x.Key, balance = x.Value.Balance, connected = x.Value.IsConnected }).ToList(),
            userConnectionKeys = _store.UserConnections.Keys.ToList(),
            activeBotsKeys = _store.ActiveBots.Keys.ToList(),
            openTradesKeys = _store.OpenTrades.Keys.ToList()
        };
    }
}

public class ConnectRequest
{
    public string UserId { get; set; } = "";
    public string Login { get; set; } = "";
    public string Password { get; set; } = "";
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
}

public class StopBotRequest
{
    public string UserId { get; set; } = "";
    public string Mt5Login { get; set; } = "";
}
