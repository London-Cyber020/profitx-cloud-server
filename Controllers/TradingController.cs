using Microsoft.AspNetCore.Mvc;

namespace ProfitX.CloudServer.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TradingController : ControllerBase
{
    private readonly DataStore _store;
    private readonly MetaApiService _metaApi;

    // Store MetaApi account IDs
    private static readonly Dictionary<string, string> _accountIds = new();

    public TradingController(DataStore store, MetaApiService metaApi)
    {
        _store = store;
        _metaApi = metaApi;
    }

    [HttpPost("startbot")]
    public object StartBot([FromBody] StartBotRequest request)
    {
        if (request == null)
            return new { success = false, message = "No data received" };

        string key = $"{request.UserId}_{request.Mt5Login}";

        if (!_store.MT5Accounts.ContainsKey(key))
        {
            return new { success = false, message = "MT5 account not connected. Connect your MT5 account first." };
        }

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

        Console.WriteLine($"BOT STARTED: {request.Symbol} {request.Strategy}");

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

        if (_store.ActiveBots.ContainsKey(key))
        {
            _store.ActiveBots[key].IsRunning = false;
            _store.ActiveBots[key].Status = "Stopped";
        }

        Console.WriteLine($"BOT STOPPED: {request.UserId}");
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
                status = bot.Status,
                runningTime = bot.IsRunning ?
                    $"{runTime.Hours:D2}:{runTime.Minutes:D2}:{runTime.Seconds:D2}" : "00:00:00"
            };
        }

        return new { success = true, isRunning = false, status = "Not started" };
    }

    [HttpGet("accountinfo")]
    public async Task<object> AccountInfo(string userId = "", string mt5Login = "")
    {
        string key = $"{userId}_{mt5Login}";

        Console.WriteLine($"AccountInfo request: key={key}");

        // Try to get fresh data from MetaApi
        if (_accountIds.ContainsKey(key))
        {
            string accountId = _accountIds[key];
            Console.WriteLine($"Fetching fresh data from MetaApi: {accountId}");

            var freshData = await _metaApi.GetAccountInfoAsync(accountId);
            if (freshData != null)
            {
                freshData.OpenTrades = (await _metaApi.GetOpenPositionsAsync(accountId)).Count;
                _store.AccountsData[key] = freshData;

                Console.WriteLine($"Fresh data: Balance=${freshData.Balance} Equity=${freshData.Equity}");
            }
        }

        // Return cached data
        if (_store.AccountsData.ContainsKey(key))
        {
            var data = _store.AccountsData[key];
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

        // Return MT5 credentials if connected but no data yet
        if (_store.MT5Accounts.ContainsKey(key))
        {
            var cred = _store.MT5Accounts[key];
            return new
            {
                success = true,
                account = new
                {
                    accountNumber = cred.Login,
                    accountName = "",
                    server = cred.Server,
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
                    isConnected = cred.IsConnected,
                    lastUpdate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                }
            };
        }

        return new
        {
            success = false,
            message = "MT5 account not connected",
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

    [HttpGet("opentrades")]
    public async Task<object> OpenTrades(string userId = "", string mt5Login = "")
    {
        string key = $"{userId}_{mt5Login}";

        // Try to get fresh trades from MetaApi
        if (_accountIds.ContainsKey(key))
        {
            string accountId = _accountIds[key];
            var freshTrades = await _metaApi.GetOpenPositionsAsync(accountId);
            if (freshTrades.Count > 0)
            {
                _store.OpenTrades[key] = freshTrades;
            }
        }

        if (_store.OpenTrades.ContainsKey(key))
        {
            var trades = _store.OpenTrades[key];
            return new
            {
                success = true,
                trades = trades.Select(t => new
                {
                    ticket = t.Ticket,
                    type = t.Type == "BUY" ? 0 : 1,
                    typeString = t.Type,
                    symbol = t.Symbol,
                    lotSize = t.LotSize,
                    entryPrice = t.EntryPrice,
                    currentPrice = t.CurrentPrice,
                    stopLoss = t.StopLoss,
                    takeProfit = t.TakeProfit,
                    profit = t.Profit,
                    swap = t.Swap,
                    commission = 0,
                    openTime = t.OpenTime,
                    isOpen = t.IsOpen
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
        {
            var trades = _store.TradeHistory[key];
            return new { success = true, trades = trades, count = trades.Count };
        }

        return new { success = true, trades = new List<object>(), count = 0 };
    }

    [HttpPost("connectmt5")]
    public async Task<object> ConnectMT5([FromBody] MT5ConnectRequest request)
    {
        if (request == null)
            return new { success = false, message = "No data received" };

        if (string.IsNullOrEmpty(request.Login) ||
            string.IsNullOrEmpty(request.Password) ||
            string.IsNullOrEmpty(request.Server))
        {
            return new { success = false, message = "All MT5 fields are required" };
        }

        string key = $"{request.UserId}_{request.Login}";

        Console.WriteLine($"═══════════════════════════════════════════");
        Console.WriteLine($"MT5 CONNECT REQUEST");
        Console.WriteLine($"  Login: {request.Login}");
        Console.WriteLine($"  Server: {request.Server}");
        Console.WriteLine($"═══════════════════════════════════════════");

        try
        {
            // Get or create MetaApi account
            string? accountId = await _metaApi.GetOrCreateAccountAsync(
                request.Login, request.Password, request.Server);

            if (accountId == null)
            {
                return new
                {
                    success = false,
                    message = "Failed to connect to MT5. Please check your credentials."
                };
            }

            // Store account ID for later use
            _accountIds[key] = accountId;

            // Store credentials
            _store.MT5Accounts[key] = new MT5Credentials
            {
                UserId = request.UserId,
                Login = request.Login,
                Password = request.Password,
                Server = request.Server,
                Platform = "mt5",
                IsConnected = true
            };

            Console.WriteLine($"Account ID stored: {accountId}");
            Console.WriteLine("Waiting for MetaApi to connect...");

            // Wait for connection then fetch account info
            // Try multiple times with increasing delay
            AccountData? accountInfo = null;
            double balance = 0;
            double equity = 0;
            string currency = "USD";
            int leverage = 0;

            for (int attempt = 1; attempt <= 3; attempt++)
            {
                Console.WriteLine($"Fetching account info (attempt {attempt}/3)...");
                await Task.Delay(5000 * attempt);

                accountInfo = await _metaApi.GetAccountInfoAsync(accountId);
                if (accountInfo != null && accountInfo.Balance > 0)
                {
                    balance = accountInfo.Balance;
                    equity = accountInfo.Equity;
                    currency = accountInfo.Currency;
                    leverage = accountInfo.Leverage;

                    _store.AccountsData[key] = accountInfo;

                    Console.WriteLine($"Account info received!");
                    Console.WriteLine($"  Balance: ${balance}");
                    Console.WriteLine($"  Equity: ${equity}");
                    break;
                }
                else
                {
                    Console.WriteLine($"  Not ready yet...");
                }
            }

            Console.WriteLine($"═══════════════════════════════════════════");
            Console.WriteLine($"MT5 CONNECTED!");
            Console.WriteLine($"  Account ID: {accountId}");
            Console.WriteLine($"  Balance: ${balance}");
            Console.WriteLine($"═══════════════════════════════════════════");

            return new
            {
                success = true,
                message = balance > 0
                    ? $"MT5 connected!\n\nBalance: ${balance:N2}\nEquity: ${equity:N2}\nLeverage: 1:{leverage}"
                    : "MT5 connected! Account data loading...\n\nPull down to refresh dashboard.",
                login = request.Login,
                server = request.Server,
                accountId = accountId,
                balance = balance,
                equity = equity,
                currency = currency,
                leverage = leverage
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"MT5 Connect ERROR: {ex.Message}");
            return new
            {
                success = false,
                message = "Connection error: " + ex.Message
            };
        }
    }

    [HttpGet("symbols")]
    public object Symbols()
    {
        return new
        {
            success = true,
            symbols = new[]
            {
                "EURUSD", "GBPUSD", "USDJPY", "USDCHF", "AUDUSD",
                "USDCAD", "NZDUSD", "EURGBP", "EURJPY", "GBPJPY",
                "EURAUD", "EURCAD", "GBPAUD", "GBPCAD",
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
