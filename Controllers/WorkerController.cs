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

    [HttpPost("connect")]
    public object Connect([FromBody] dynamic request)
    {
        try
        {
            string userId = request?.userId?.ToString() ?? "";
            string mt5Login = request?.mt5Login?.ToString() ?? "";
            string server = request?.server?.ToString() ?? "";

            string key = $"{userId}_{mt5Login}";

            Console.WriteLine($"Worker connect: {key}");

            _store.UserConnections[key] = new UserMT5Connection
            {
                UserId = userId,
                Mt5Login = mt5Login,
                Mt5Server = server,
                IsConnected = true,
                ConnectedAt = DateTime.Now
            };

            // Also store account data if provided
            if (request?.account != null)
            {
                try
                {
                    var acc = request.account;
                    _store.AccountsData[key] = new AccountData
                    {
                        AccountNumber = acc.accountNumber?.ToString() ?? "",
                        AccountName = acc.accountName?.ToString() ?? "",
                        Server = acc.server?.ToString() ?? "",
                        Currency = acc.currency?.ToString() ?? "USD",
                        Leverage = (int)(acc.leverage ?? 0),
                        Balance = (double)(acc.balance ?? 0),
                        Equity = (double)(acc.equity ?? 0),
                        Margin = (double)(acc.margin ?? 0),
                        FreeMargin = (double)(acc.freeMargin ?? 0),
                        MarginLevel = (double)(acc.marginLevel ?? 0),
                        Drawdown = (double)(acc.currentDrawdown ?? 0),
                        ProfitToday = (double)(acc.profitToday ?? 0),
                        OpenTrades = (int)(acc.openTrades ?? 0),
                        IsConnected = true,
                        LastUpdate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                    };

                    Console.WriteLine($"Account data stored: Balance=${_store.AccountsData[key].Balance}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Account parse error: {ex.Message}");
                }
            }

            return new { success = true, message = "Registered" };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Connect error: {ex.Message}");
            return new { success = false, message = ex.Message };
        }
    }

    [HttpPost("accountinfo")]
    public object AccountInfo([FromBody] dynamic request)
    {
        try
        {
            string userId = request?.userId?.ToString() ?? "";
            string mt5Login = request?.mt5Login?.ToString() ?? "";
            string key = $"{userId}_{mt5Login}";

            Console.WriteLine($"Worker accountinfo: {key}");

            if (request?.data != null)
            {
                var d = request.data;
                _store.AccountsData[key] = new AccountData
                {
                    AccountNumber = d.accountNumber?.ToString() ?? "",
                    AccountName = d.accountName?.ToString() ?? "",
                    Server = d.server?.ToString() ?? "",
                    Currency = d.currency?.ToString() ?? "USD",
                    Leverage = (int)(d.leverage ?? 0),
                    Balance = (double)(d.balance ?? 0),
                    Equity = (double)(d.equity ?? 0),
                    Margin = (double)(d.margin ?? 0),
                    FreeMargin = (double)(d.freeMargin ?? 0),
                    MarginLevel = (double)(d.marginLevel ?? 0),
                    Drawdown = (double)(d.currentDrawdown ?? 0),
                    ProfitToday = (double)(d.profitToday ?? 0),
                    OpenTrades = (int)(d.openTrades ?? 0),
                    IsConnected = true,
                    LastUpdate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                };

                Console.WriteLine($"Account updated: Balance=${_store.AccountsData[key].Balance}");
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
    public object OpenTrades([FromBody] dynamic request)
    {
        try
        {
            string userId = request?.userId?.ToString() ?? "";
            string mt5Login = request?.mt5Login?.ToString() ?? "";
            string key = $"{userId}_{mt5Login}";

            if (request?.trades != null)
            {
                var tradeList = new List<TradeData>();

                foreach (var t in request.trades)
                {
                    try
                    {
                        tradeList.Add(new TradeData
                        {
                            Ticket = (long)(t.ticket ?? 0),
                            Type = t.typeString?.ToString() ?? "",
                            Symbol = t.symbol?.ToString() ?? "",
                            LotSize = (double)(t.lotSize ?? 0),
                            EntryPrice = (double)(t.entryPrice ?? 0),
                            CurrentPrice = (double)(t.currentPrice ?? 0),
                            StopLoss = (double)(t.stopLoss ?? 0),
                            TakeProfit = (double)(t.takeProfit ?? 0),
                            Profit = (double)(t.profit ?? 0),
                            Swap = (double)(t.swap ?? 0),
                            OpenTime = t.openTime?.ToString() ?? "",
                            IsOpen = true
                        });
                    }
                    catch { }
                }

                _store.OpenTrades[key] = tradeList;
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
}
