using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

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

    [HttpPost("connect")]
    public object Connect([FromBody] JsonElement body)
    {
        try
        {
            string userId = body.TryGetProperty("userId", out var u) ? u.GetString() ?? "" : "";
            string mt5Login = body.TryGetProperty("mt5Login", out var m) ? m.GetString() ?? "" : "";
            string server = body.TryGetProperty("server", out var s) ? s.GetString() ?? "" : "";

            string key = $"{userId}_{mt5Login}";

            Console.WriteLine($"Worker CONNECT: key={key}");

            _store.UserConnections[key] = new UserMT5Connection
            {
                UserId = userId,
                Mt5Login = mt5Login,
                Mt5Server = server,
                IsConnected = true,
                ConnectedAt = DateTime.Now
            };

            // Parse account data
            if (body.TryGetProperty("account", out var acc))
            {
                var data = ParseAccountData(acc);
                if (data != null && data.Balance > 0)
                {
                    _store.AccountsData[key] = data;
                    Console.WriteLine($"Account stored: Balance=${data.Balance} Equity=${data.Equity}");
                }
            }

            Console.WriteLine($"Worker registered: {key}");
            return new { success = true, message = "Registered" };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Connect error: {ex.Message}");
            return new { success = false, message = ex.Message };
        }
    }

    [HttpPost("accountinfo")]
    public object AccountInfo([FromBody] JsonElement body)
    {
        try
        {
            string userId = body.TryGetProperty("userId", out var u) ? u.GetString() ?? "" : "";
            string mt5Login = body.TryGetProperty("mt5Login", out var m) ? m.GetString() ?? "" : "";
            string key = $"{userId}_{mt5Login}";

            if (body.TryGetProperty("data", out var dataElement))
            {
                var data = ParseAccountData(dataElement);
                if (data != null)
                {
                    _store.AccountsData[key] = data;
                    Console.WriteLine($"Account updated: key={key} Balance=${data.Balance}");
                }
            }

            return new { success = true };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"AccountInfo error: {ex.Message}");
            return new { success = false, message = ex.Message };
        }
    }

    [HttpPost("opentrades")]
    public object OpenTrades([FromBody] JsonElement body)
    {
        try
        {
            string userId = body.TryGetProperty("userId", out var u) ? u.GetString() ?? "" : "";
            string mt5Login = body.TryGetProperty("mt5Login", out var m) ? m.GetString() ?? "" : "";
            string key = $"{userId}_{mt5Login}";

            if (body.TryGetProperty("trades", out var tradesElement))
            {
                var trades = new List<TradeData>();

                foreach (var t in tradesElement.EnumerateArray())
                {
                    try
                    {
                        trades.Add(new TradeData
                        {
                            Ticket = t.TryGetProperty("ticket", out var tk) ? tk.GetInt64() : 0,
                            Type = t.TryGetProperty("typeString", out var ts) ? ts.GetString() ?? "" : "",
                            Symbol = t.TryGetProperty("symbol", out var sy) ? sy.GetString() ?? "" : "",
                            LotSize = t.TryGetProperty("lotSize", out var ls) ? ls.GetDouble() : 0,
                            EntryPrice = t.TryGetProperty("entryPrice", out var ep) ? ep.GetDouble() : 0,
                            CurrentPrice = t.TryGetProperty("currentPrice", out var cp) ? cp.GetDouble() : 0,
                            StopLoss = t.TryGetProperty("stopLoss", out var sl) ? sl.GetDouble() : 0,
                            TakeProfit = t.TryGetProperty("takeProfit", out var tp) ? tp.GetDouble() : 0,
                            Profit = t.TryGetProperty("profit", out var pr) ? pr.GetDouble() : 0,
                            Swap = t.TryGetProperty("swap", out var sw) ? sw.GetDouble() : 0,
                            OpenTime = t.TryGetProperty("openTime", out var ot) ? ot.GetString() ?? "" : "",
                            IsOpen = true
                        });
                    }
                    catch { }
                }

                _store.OpenTrades[key] = trades;
                Console.WriteLine($"Trades updated: key={key} count={trades.Count}");
            }

            return new { success = true };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"OpenTrades error: {ex.Message}");
            return new { success = false, message = ex.Message };
        }
    }

    [HttpGet("commands")]
    public object Commands(string userId = "", string mt5Login = "")
    {
        string key = $"{userId}_{mt5Login}";

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

    private AccountData? ParseAccountData(JsonElement element)
    {
        try
        {
            return new AccountData
            {
                AccountNumber = element.TryGetProperty("accountNumber", out var an) ? an.GetString() ?? "" : "",
                AccountName = element.TryGetProperty("accountName", out var name) ? name.GetString() ?? "" : "",
                Server = element.TryGetProperty("server", out var srv) ? srv.GetString() ?? "" : "",
                Currency = element.TryGetProperty("currency", out var cur) ? cur.GetString() ?? "USD" : "USD",
                Leverage = element.TryGetProperty("leverage", out var lev) ? lev.GetInt32() : 0,
                Balance = element.TryGetProperty("balance", out var bal) ? bal.GetDouble() : 0,
                Equity = element.TryGetProperty("equity", out var eq) ? eq.GetDouble() : 0,
                Margin = element.TryGetProperty("margin", out var mar) ? mar.GetDouble() : 0,
                FreeMargin = element.TryGetProperty("freeMargin", out var fm) ? fm.GetDouble() : 0,
                MarginLevel = element.TryGetProperty("marginLevel", out var ml) ? ml.GetDouble() : 0,
                Drawdown = element.TryGetProperty("currentDrawdown", out var dd) ? dd.GetDouble() : 0,
                ProfitToday = element.TryGetProperty("profitToday", out var pt) ? pt.GetDouble() : 0,
                OpenTrades = element.TryGetProperty("openTrades", out var ot) ? ot.GetInt32() : 0,
                IsConnected = true,
                LastUpdate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ParseAccountData error: {ex.Message}");
            return null;
        }
    }
}
