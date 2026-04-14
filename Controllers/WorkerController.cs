using Microsoft.AspNetCore.Mvc;

namespace ProfitX.CloudServer.Controllers;

[ApiController]
[Route("api/[controller]")]
public class WorkerController : ControllerBase
{
    private readonly DataStore _store;

    public WorkerController(DataStore store)
    {
        _store = store;
    }

    [HttpPost("accountinfo")]
    public object AccountInfo([FromBody] WorkerAccountRequest request)
    {
        if (request == null) return new { success = false };

        string key = $"{request.UserId}_{request.Mt5Login}";

        if (request.Data != null)
        {
            _store.AccountsData[key] = new AccountData
            {
                AccountNumber = request.Data.GetValueOrDefault("accountNumber", ""),
                AccountName = request.Data.GetValueOrDefault("accountName", ""),
                Server = request.Data.GetValueOrDefault("server", ""),
                Currency = request.Data.GetValueOrDefault("currency", "USD"),
                Leverage = int.TryParse(request.Data.GetValueOrDefault("leverage", "0"), out int lev) ? lev : 0,
                Balance = double.TryParse(request.Data.GetValueOrDefault("balance", "0"), out double bal) ? bal : 0,
                Equity = double.TryParse(request.Data.GetValueOrDefault("equity", "0"), out double eq) ? eq : 0,
                Margin = double.TryParse(request.Data.GetValueOrDefault("margin", "0"), out double mar) ? mar : 0,
                FreeMargin = double.TryParse(request.Data.GetValueOrDefault("freeMargin", "0"), out double fm) ? fm : 0,
                MarginLevel = double.TryParse(request.Data.GetValueOrDefault("marginLevel", "0"), out double ml) ? ml : 0,
                Drawdown = double.TryParse(request.Data.GetValueOrDefault("currentDrawdown", "0"), out double dd) ? dd : 0,
                ProfitToday = double.TryParse(request.Data.GetValueOrDefault("profitToday", "0"), out double pt) ? pt : 0,
                OpenTrades = int.TryParse(request.Data.GetValueOrDefault("openTrades", "0"), out int ot) ? ot : 0,
                IsConnected = true,
                LastUpdate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            };
        }

        return new { success = true };
    }

    [HttpPost("opentrades")]
    public object OpenTrades([FromBody] WorkerTradesRequest request)
    {
        if (request == null) return new { success = false };

        string key = $"{request.UserId}_{request.Mt5Login}";

        if (request.Trades != null)
        {
            var tradeList = new List<TradeData>();
            foreach (var t in request.Trades)
            {
                tradeList.Add(new TradeData
                {
                    Ticket = t.GetValueOrDefault("ticket", 0L) is long tk ? tk : 0,
                    Type = t.GetValueOrDefault("typeString", "")?.ToString() ?? "",
                    Symbol = t.GetValueOrDefault("symbol", "")?.ToString() ?? "",
                    LotSize = t.GetValueOrDefault("lotSize", 0.0) is double ls ? ls : 0,
                    EntryPrice = t.GetValueOrDefault("entryPrice", 0.0) is double ep ? ep : 0,
                    CurrentPrice = t.GetValueOrDefault("currentPrice", 0.0) is double cp ? cp : 0,
                    StopLoss = t.GetValueOrDefault("stopLoss", 0.0) is double sl ? sl : 0,
                    TakeProfit = t.GetValueOrDefault("takeProfit", 0.0) is double tp ? tp : 0,
                    Profit = t.GetValueOrDefault("profit", 0.0) is double pr ? pr : 0,
                    Swap = t.GetValueOrDefault("swap", 0.0) is double sw ? sw : 0,
                    OpenTime = t.GetValueOrDefault("openTime", "")?.ToString() ?? "",
                    IsOpen = true
                });
            }
            _store.OpenTrades[key] = tradeList;
        }

        return new { success = true };
    }

    [HttpPost("connect")]
    public object Connect([FromBody] WorkerConnectRequest request)
    {
        if (request == null) return new { success = false };

        string key = $"{request.UserId}_{request.Mt5Login}";

        _store.UserConnections[key] = new UserMT5Connection
        {
            UserId = request.UserId,
            Mt5Login = request.Mt5Login,
            Mt5Server = request.Server,
            IsConnected = true,
            ConnectedAt = DateTime.Now
        };

        if (request.Account != null)
        {
            _store.AccountsData[key] = new AccountData
            {
                AccountNumber = request.Account.GetValueOrDefault("accountNumber", "")?.ToString() ?? "",
                AccountName = request.Account.GetValueOrDefault("accountName", "")?.ToString() ?? "",
                Server = request.Account.GetValueOrDefault("server", "")?.ToString() ?? "",
                Currency = request.Account.GetValueOrDefault("currency", "USD")?.ToString() ?? "USD",
                Leverage = Convert.ToInt32(request.Account.GetValueOrDefault("leverage", 0)),
                Balance = Convert.ToDouble(request.Account.GetValueOrDefault("balance", 0.0)),
                Equity = Convert.ToDouble(request.Account.GetValueOrDefault("equity", 0.0)),
                Margin = Convert.ToDouble(request.Account.GetValueOrDefault("margin", 0.0)),
                FreeMargin = Convert.ToDouble(request.Account.GetValueOrDefault("freeMargin", 0.0)),
                MarginLevel = Convert.ToDouble(request.Account.GetValueOrDefault("marginLevel", 0.0)),
                Drawdown = Convert.ToDouble(request.Account.GetValueOrDefault("currentDrawdown", 0.0)),
                OpenTrades = Convert.ToInt32(request.Account.GetValueOrDefault("openTrades", 0)),
                IsConnected = true,
                LastUpdate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            };
        }

        Console.WriteLine($"Worker connected: {request.Mt5Login} on {request.Server}");

        return new { success = true, message = "Registered" };
    }

    [HttpGet("commands")]
    public object Commands(string userId = "", string mt5Login = "")
    {
        string key = $"{userId}_{mt5Login}";

        // Check if there's a pending bot start/stop
        if (_store.ActiveBots.ContainsKey(key))
        {
            var bot = _store.ActiveBots[key];
            return new
            {
                success = true,
                command = bot.IsRunning ? "START" : "STOP",
                symbol = bot.Symbol,
                strategy = bot.Strategy,
                lotSize = bot.LotSize,
                maxTrades = bot.MaxTrades
            };
        }

        return new { success = true, command = "NONE" };
    }
}

public class WorkerAccountRequest
{
    public string UserId { get; set; } = "";
    public string Mt5Login { get; set; } = "";
    public Dictionary<string, string>? Data { get; set; }
}

public class WorkerTradesRequest
{
    public string UserId { get; set; } = "";
    public string Mt5Login { get; set; } = "";
    public List<Dictionary<string, object>>? Trades { get; set; }
}

public class WorkerConnectRequest
{
    public string UserId { get; set; } = "";
    public string Mt5Login { get; set; } = "";
    public string Server { get; set; } = "";
    public Dictionary<string, object>? Account { get; set; }
}
