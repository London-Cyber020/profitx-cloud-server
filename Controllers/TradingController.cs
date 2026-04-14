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
            IsConnected = false,
            ConnectedAt = DateTime.Now
        };

        Console.WriteLine($"MT5 credentials stored: {request.Login} on {request.Server}");
        Console.WriteLine("Waiting for Python Worker to connect...");

        return new
        {
            success = true,
            message = "MT5 credentials saved!\n\nMake sure Python Worker is running on your PC.\nPull down to refresh dashboard.",
            login = request.Login,
            server = request.Server
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
        return new { success = true, message = $"Bot started!\n\nSymbol: {request.Symbol}\nStrategy: {request.Strategy}", symbol = request.Symbol, strategy = request.Strategy, lotSize = request.LotSize };
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
            return new { success = true, isRunning = bot.IsRunning, symbol = bot.Symbol, strategy = bot.Strategy, lotSize = bot.LotSize, maxTrades = bot.MaxTrades, status = bot.Status, runningTime = bot.IsRunning ? $"{rt.Hours:D2}:{rt.Minutes:D2}:{rt.Seconds:D2}" : "00:00:00" };
        }
        return new { success = true, isRunning = false, status = "Not started" };
    }

    [HttpGet("accountinfo")]
    public object AccountInfo(string userId = "", string mt5Login = "")
    {
        string key = $"{userId}_{mt5Login}";

        if (_store.AccountsData.ContainsKey(key))
        {
            var d = _store.AccountsData[key];
            return new { success = true, account = new { accountNumber = d.AccountNumber, accountName = d.AccountName, server = d.Server, currency = d.Currency, leverage = d.Leverage, balance = d.Balance, equity = d.Equity, margin = d.Margin, freeMargin = d.FreeMargin, marginLevel = d.MarginLevel, currentDrawdown = d.Drawdown, profitToday = d.ProfitToday, profitThisWeek = 0.0, profitThisMonth = 0.0, totalTrades = 0, winningTrades = 0, losingTrades = 0, openTrades = d.OpenTrades, isConnected = d.IsConnected, lastUpdate = d.LastUpdate } };
        }

        if (_store.UserConnections.ContainsKey(key))
        {
            var c = _store.UserConnections[key];
            return new { success = true, account = new { accountNumber = c.Mt5Login, accountName = "", server = c.Mt5Server, currency = "USD", leverage = 0, balance = 0.0, equity = 0.0, margin = 0.0, freeMargin = 0.0, marginLevel = 0.0, currentDrawdown = 0.0, profitToday = 0.0, profitThisWeek = 0.0, profitThisMonth = 0.0, totalTrades = 0, winningTrades = 0, losingTrades = 0, openTrades = 0, isConnected = c.IsConnected, lastUpdate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") } };
        }

        return new { success = false, message = "MT5 not connected", account = new { accountNumber = "", accountName = "", server = "", currency = "USD", leverage = 0, balance = 0.0, equity = 0.0, margin = 0.0, freeMargin = 0.0, marginLevel = 0.0, currentDrawdown = 0.0, profitToday = 0.0, profitThisWeek = 0.0, profitThisMonth = 0.0, totalTrades = 0, winningTrades = 0, losingTrades = 0, openTrades = 0, isConnected = false, lastUpdate = "" } };
    }

    [HttpGet("opentrades")]
    public object OpenTrades(string userId = "", string mt5Login = "")
    {
        string key = $"{userId}_{mt5Login}";
        if (_store.OpenTrades.ContainsKey(key))
        {
            var t = _store.OpenTrades[key];
            return new { success = true, trades = t.Select(x => new { ticket = x.Ticket, type = x.Type == "BUY" ? 0 : 1, typeString = x.Type, symbol = x.Symbol, lotSize = x.LotSize, entryPrice = x.EntryPrice, currentPrice = x.CurrentPrice, stopLoss = x.StopLoss, takeProfit = x.TakeProfit, profit = x.Profit, swap = x.Swap, commission = 0, openTime = x.OpenTime, isOpen = x.IsOpen }), count = t.Count };
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
    public object Symbols() => new { success = true, symbols = new[] { "EURUSD", "GBPUSD", "USDJPY", "USDCHF", "AUDUSD", "USDCAD", "NZDUSD", "EURGBP", "EURJPY", "GBPJPY", "XAUUSD", "XAGUSD", "BTCUSD", "ETHUSD", "US30", "US100", "US500", "USOIL" } };

    [HttpGet("strategies")]
    public object Strategies() => new { success = true, strategies = new object[] { new { name = "ICT", fullName = "Inner Circle Trader" }, new { name = "SMC", fullName = "Smart Money Concepts" } } };
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
