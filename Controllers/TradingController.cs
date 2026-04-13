using System;
using System.Collections.Generic;
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

    [HttpPost("startbot")]
    public object StartBot([FromBody] StartBotRequest request)
    {
        if (request == null)
            return new { success = false, message = "No data received" };

        string command = $"START|{request.Symbol}|{request.LotSize}|{request.Strategy}|{request.MaxTrades}";
        string key = $"{request.UserId}_{request.Mt5Login}";

        _store.PendingCommands[key] = command;
        _store.ActiveBots[key] = new BotSession
        {
            UserId = request.UserId,
            Mt5Login = request.Mt5Login,
            Symbol = request.Symbol,
            LotSize = request.LotSize,
            Strategy = request.Strategy,
            MaxTrades = request.MaxTrades,
            IsRunning = true,
            StartTime = DateTime.Now
        };

        Console.WriteLine($"BOT STARTED: {request.Symbol} {request.Strategy} Lot:{request.LotSize}");

        return new
        {
            success = true,
            message = "Trading bot started successfully!",
            symbol = request.Symbol,
            strategy = request.Strategy,
            lotSize = request.LotSize
        };
    }

    [HttpPost("stopbot")]
    public object StopBot([FromBody] StopBotRequest request)
    {
        if (request == null)
            return new { success = false, message = "No data received" };

        string key = $"{request.UserId}_{request.Mt5Login}";
        _store.PendingCommands[key] = "STOP";

        if (_store.ActiveBots.ContainsKey(key))
        {
            _store.ActiveBots[key].IsRunning = false;
        }

        Console.WriteLine("BOT STOPPED for user: " + request.UserId);
        return new { success = true, message = "Trading bot stopped!" };
    }

    [HttpGet("botstatus")]
    public object BotStatus(string userId = "", string mt5Login = "")
    {
        string key = $"{userId}_{mt5Login}";

        if (_store.ActiveBots.ContainsKey(key))
        {
            var bot = _store.ActiveBots[key];
            var runTime = DateTime.Now - bot.StartTime;

            return new
            {
                success = true,
                isRunning = bot.IsRunning,
                symbol = bot.Symbol,
                strategy = bot.Strategy,
                lotSize = bot.LotSize,
                maxTrades = bot.MaxTrades,
                runningTime = $"{runTime.Hours:D2}:{runTime.Minutes:D2}:{runTime.Seconds:D2}"
            };
        }

        return new { success = true, isRunning = false };
    }

    [HttpGet("accountinfo")]
    public object AccountInfo(string userId = "", string mt5Login = "")
    {
        string key = $"{userId}_{mt5Login}";

        if (_store.PcData.ContainsKey(key) && _store.PcData[key].ContainsKey("accountInfo"))
        {
            var content = _store.PcData[key]["accountInfo"];
            var data = new Dictionary<string, string>();

            foreach (var line in content.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                if (line.Contains('='))
                {
                    var parts = line.Split('=', 2);
                    data[parts[0].Trim()] = parts[1].Trim();
                }
            }

            return new
            {
                success = true,
                account = new
                {
                    accountNumber = data.GetValueOrDefault("accountNumber", "0"),
                    accountName = data.GetValueOrDefault("accountName", ""),
                    server = data.GetValueOrDefault("server", ""),
                    currency = data.GetValueOrDefault("currency", "USD"),
                    leverage = int.TryParse(data.GetValueOrDefault("leverage", "0"), out int lev) ? lev : 0,
                    balance = double.TryParse(data.GetValueOrDefault("balance", "0"), out double bal) ? bal : 0,
                    equity = double.TryParse(data.GetValueOrDefault("equity", "0"), out double eq) ? eq : 0,
                    margin = double.TryParse(data.GetValueOrDefault("margin", "0"), out double mar) ? mar : 0,
                    freeMargin = double.TryParse(data.GetValueOrDefault("freeMargin", "0"), out double fm) ? fm : 0,
                    marginLevel = double.TryParse(data.GetValueOrDefault("marginLevel", "0"), out double ml) ? ml : 0,
                    currentDrawdown = double.TryParse(data.GetValueOrDefault("drawdown", "0"), out double dd) ? dd : 0,
                    profitToday = double.TryParse(data.GetValueOrDefault("profitToday", "0"), out double pt) ? pt : 0,
                    profitThisWeek = double.TryParse(data.GetValueOrDefault("profitWeek", "0"), out double pw) ? pw : 0,
                    profitThisMonth = double.TryParse(data.GetValueOrDefault("profitMonth", "0"), out double pm) ? pm : 0,
                    totalTrades = int.TryParse(data.GetValueOrDefault("totalTrades", "0"), out int tt) ? tt : 0,
                    winningTrades = int.TryParse(data.GetValueOrDefault("winningTrades", "0"), out int wt) ? wt : 0,
                    losingTrades = int.TryParse(data.GetValueOrDefault("losingTrades", "0"), out int lt) ? lt : 0,
                    openTrades = int.TryParse(data.GetValueOrDefault("openTrades", "0"), out int ot) ? ot : 0,
                    isConnected = true,
                    lastUpdate = data.GetValueOrDefault("lastUpdate", "")
                }
            };
        }

        return new
        {
            success = false,
            message = "PC not connected",
            account = new
            {
                balance = 0, equity = 0, margin = 0, freeMargin = 0,
                marginLevel = 0, currentDrawdown = 0, profitToday = 0,
                profitThisWeek = 0, profitThisMonth = 0, totalTrades = 0,
                winningTrades = 0, losingTrades = 0, openTrades = 0,
                isConnected = false
            }
        };
    }

    [HttpGet("opentrades")]
    public object OpenTrades(string userId = "", string mt5Login = "")
    {
        string key = $"{userId}_{mt5Login}";

        if (_store.PcData.ContainsKey(key) && _store.PcData[key].ContainsKey("openTrades"))
        {
            var content = _store.PcData[key]["openTrades"];
            var trades = new List<object>();

            foreach (var line in content.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = line.Split('|');
                if (parts.Length >= 11)
                {
                    trades.Add(new
                    {
                        ticket = long.TryParse(parts[0], out long tk) ? tk : 0,
                        type = parts[1] == "BUY" ? 0 : 1,
                        typeString = parts[1],
                        symbol = parts[2],
                        lotSize = double.TryParse(parts[3], out double ls) ? ls : 0,
                        entryPrice = double.TryParse(parts[4], out double ep) ? ep : 0,
                        currentPrice = double.TryParse(parts[5], out double cp) ? cp : 0,
                        stopLoss = double.TryParse(parts[6], out double slv) ? slv : 0,
                        takeProfit = double.TryParse(parts[7], out double tpv) ? tpv : 0,
                        profit = double.TryParse(parts[8], out double pr) ? pr : 0,
                        swap = double.TryParse(parts[9], out double sw) ? sw : 0,
                        commission = 0,
                        openTime = parts[10],
                        isOpen = true
                    });
                }
            }

            return new { success = true, trades = trades, count = trades.Count };
        }

        return new { success = true, trades = new List<object>(), count = 0 };
    }

    [HttpGet("history")]
    public object History(string userId = "", int days = 30)
    {
        return new { success = true, trades = new List<object>(), count = 0 };
    }

    [HttpPost("connectmt5")]
    public object ConnectMT5([FromBody] MT5ConnectRequest request)
    {
        if (request == null)
            return new { success = false, message = "No data received" };

        Console.WriteLine($"MT5 Connected: Login:{request.Login} Server:{request.Server}");
        return new { success = true, message = "MT5 account connected successfully!" };
    }

    [HttpGet("symbols")]
    public object Symbols()
    {
        return new
        {
            success = true,
            symbols = new[]
            {
                "EURUSD", "GBPUSD", "USDJPY", "USDCHF", "AUDUSD", "USDCAD", "NZDUSD",
                "EURGBP", "EURJPY", "GBPJPY", "EURAUD", "EURCAD",
                "XAUUSD", "XAGUSD",
                "BTCUSD", "ETHUSD",
                "US30", "US100", "US500",
                "USOIL", "UKOIL"
            }
        };
    }

    [HttpGet("strategies")]
    public object Strategies()
    {
        return new
        {
            success = true,
            strategies = new object[]
            {
                new { name = "ICT", fullName = "Inner Circle Trader", description = "Order Blocks + FVG" },
                new { name = "SMC", fullName = "Smart Money Concepts", description = "BOS + Market Structure" }
            }
        };
    }
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

public class MT5ConnectRequest
{
    public string UserId { get; set; } = "";
    public string Login { get; set; } = "";
    public string Password { get; set; } = "";
    public string Server { get; set; } = "";
}
