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

    // ── Worker Connect / Register ─────────────────────────────
    [HttpPost("connect")]
    public object Connect([FromBody] JsonElement body)
    {
        try
        {
            string userId   = GetString(body, "userId");
            string mt5Login = GetString(body, "mt5Login");
            string server   = GetString(body, "server");
            string key      = $"{userId}_{mt5Login}";

            Console.WriteLine($"Worker CONNECT: key={key}");

            // Register connection (no password stored)
            _store.UserConnections[key] = new UserMT5Connection
            {
                UserId      = userId,
                Mt5Login    = mt5Login,
                Mt5Server   = server,
                IsConnected = true,
                ConnectedAt = DateTime.UtcNow
            };

            // Record heartbeat
            _store.UpdateWorkerHeartbeat(key);

            // Store account data if provided
            if (body.TryGetProperty("account", out var acc))
            {
                var data = ParseAccountData(acc);
                if (data != null && data.Balance > 0)
                {
                    _store.AccountsData[key] = data;
                    Console.WriteLine(
                        $"Account stored: Balance=${data.Balance} " +
                        $"Equity=${data.Equity}");
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

    // ── Account Info Push ─────────────────────────────────────
    [HttpPost("accountinfo")]
    public object AccountInfo([FromBody] JsonElement body)
    {
        try
        {
            string userId   = GetString(body, "userId");
            string mt5Login = GetString(body, "mt5Login");
            string key      = $"{userId}_{mt5Login}";

            // Update heartbeat on every push
            _store.UpdateWorkerHeartbeat(key);

            if (body.TryGetProperty("data", out var dataElement))
            {
                var data = ParseAccountData(dataElement);
                if (data != null)
                {
                    _store.AccountsData[key] = data;
                    Console.WriteLine(
                        $"Account updated: key={key} " +
                        $"Balance=${data.Balance} " +
                        $"ProfitToday=${data.ProfitToday}");
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

    // ── Open Trades Push ──────────────────────────────────────
    [HttpPost("opentrades")]
    public object OpenTrades([FromBody] JsonElement body)
    {
        try
        {
            string userId   = GetString(body, "userId");
            string mt5Login = GetString(body, "mt5Login");
            string key      = $"{userId}_{mt5Login}";

            // Update heartbeat
            _store.UpdateWorkerHeartbeat(key);

            if (body.TryGetProperty("trades", out var tradesElement))
            {
                var trades = new List<TradeData>();

                foreach (var t in tradesElement.EnumerateArray())
                {
                    try
                    {
                        trades.Add(new TradeData
                        {
                            Ticket       = GetLong(t, "ticket"),
                            Type         = GetString(t, "typeString"),
                            Symbol       = GetString(t, "symbol"),
                            LotSize      = GetDouble(t, "lotSize"),
                            EntryPrice   = GetDouble(t, "entryPrice"),
                            CurrentPrice = GetDouble(t, "currentPrice"),
                            StopLoss     = GetDouble(t, "stopLoss"),
                            TakeProfit   = GetDouble(t, "takeProfit"),
                            Profit       = GetDouble(t, "profit"),
                            Swap         = GetDouble(t, "swap"),
                            OpenTime     = GetString(t, "openTime"),
                            IsOpen       = true
                        });
                    }
                    catch { /* Skip malformed trade */ }
                }

                _store.OpenTrades[key] = trades;
                Console.WriteLine(
                    $"Trades updated: key={key} count={trades.Count}");
            }

            return new { success = true };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"OpenTrades error: {ex.Message}");
            return new { success = false, message = ex.Message };
        }
    }

    // ── Trade History Push ────────────────────────────────────
    [HttpPost("tradehistory")]
    public object TradeHistory([FromBody] JsonElement body)
    {
        try
        {
            string userId   = GetString(body, "userId");
            string mt5Login = GetString(body, "mt5Login");
            string key      = $"{userId}_{mt5Login}";

            _store.UpdateWorkerHeartbeat(key);

            if (body.TryGetProperty("trades", out var tradesElement))
            {
                var trades = new List<TradeData>();

                foreach (var t in tradesElement.EnumerateArray())
                {
                    try
                    {
                        trades.Add(new TradeData
                        {
                            Ticket       = GetLong(t, "ticket"),
                            Type         = GetString(t, "typeString"),
                            Symbol       = GetString(t, "symbol"),
                            LotSize      = GetDouble(t, "lotSize"),
                            EntryPrice   = GetDouble(t, "entryPrice"),
                            CurrentPrice = GetDouble(t, "currentPrice"),
                            Profit       = GetDouble(t, "profit"),
                            Swap         = GetDouble(t, "swap"),
                            OpenTime     = GetString(t, "openTime"),
                            CloseTime    = GetString(t, "closeTime"),
                            IsOpen       = false
                        });
                    }
                    catch { /* Skip malformed */ }
                }

                _store.TradeHistory[key] = trades;
                Console.WriteLine(
                    $"History updated: key={key} count={trades.Count}");
            }

            return new { success = true };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"TradeHistory error: {ex.Message}");
            return new { success = false, message = ex.Message };
        }
    }

    // ── Commands Poll ─────────────────────────────────────────
    [HttpGet("commands")]
    public object Commands(string userId = "", string mt5Login = "")
    {
        string key = $"{userId}_{mt5Login}";

        // Update heartbeat every poll - this is the liveness signal
        _store.UpdateWorkerHeartbeat(key);

        if (_store.ActiveBots.TryGetValue(key, out var bot))
        {
            return new
            {
                success      = true,
                command      = bot.IsRunning ? "START" : "STOP",
                symbol       = bot.Symbol,
                strategy     = bot.Strategy,
                lotSize      = bot.LotSize,
                maxTrades    = bot.MaxTrades,
                maxDailyLoss = bot.MaxDailyLoss,
                maxDrawdown  = bot.MaxDrawdown
            };
        }

        return new { success = true, command = "NONE" };
    }

    // ── Private Helpers ───────────────────────────────────────
    private static string GetString(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) ? v.GetString() ?? "" : "";

    private static double GetDouble(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) ? v.GetDouble() : 0;

    private static long GetLong(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) ? v.GetInt64() : 0;

    private static int GetInt(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) ? v.GetInt32() : 0;

    private AccountData? ParseAccountData(JsonElement element)
    {
        try
        {
            return new AccountData
            {
                AccountNumber = GetString(element, "accountNumber"),
                AccountName   = GetString(element, "accountName"),
                Server        = GetString(element, "server"),
                Currency      = element.TryGetProperty("currency", out var cur)
                                ? cur.GetString() ?? "USD" : "USD",
                Leverage      = GetInt(element, "leverage"),
                Balance       = GetDouble(element, "balance"),
                Equity        = GetDouble(element, "equity"),
                Margin        = GetDouble(element, "margin"),
                FreeMargin    = GetDouble(element, "freeMargin"),
                MarginLevel   = GetDouble(element, "marginLevel"),
                Drawdown      = GetDouble(element, "currentDrawdown"),
                ProfitToday   = GetDouble(element, "profitToday"),
                ProfitThisWeek  = GetDouble(element, "profitThisWeek"),
                ProfitThisMonth = GetDouble(element, "profitThisMonth"),
                TotalTrades   = GetInt(element, "totalTrades"),
                WinningTrades = GetInt(element, "winningTrades"),
                LosingTrades  = GetInt(element, "losingTrades"),
                OpenTrades    = GetInt(element, "openTrades"),
                IsConnected   = true,
                LastUpdate    = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ParseAccountData error: {ex.Message}");
            return null;
        }
    }
}
