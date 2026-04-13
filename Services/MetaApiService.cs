using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public class MetaApiService
{
    private readonly HttpClient _httpClient;
    private readonly string _apiToken;
    private readonly string _apiUrl = "https://mt-provisioning-api-v1.agiliumtrade.agiliumtrade.ai";
    private readonly DataStore _store;
    
    // Cache account IDs to avoid re-creating
    private readonly Dictionary<string, string> _accountIdCache = new();

    public MetaApiService(DataStore store)
    {
        _store = store;
        _apiToken = Environment.GetEnvironmentVariable("METAAPI_TOKEN") ?? "";

        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("auth-token", _apiToken);
        _httpClient.Timeout = TimeSpan.FromSeconds(30);

        Console.WriteLine("MetaApi Service initialized");
        Console.WriteLine("Token: " + (_apiToken.Length > 10 ? _apiToken.Substring(0, 10) + "..." : "NOT SET"));
    }

    // ═══════════════════════════════════════════════════════════════
    // GET OR CREATE ACCOUNT (checks existing first!)
    // ═══════════════════════════════════════════════════════════════
    public async Task<string?> GetOrCreateAccountAsync(string login, string password, string server)
    {
        try
        {
            Console.WriteLine($"GetOrCreateAccount for login: {login}");

            // Check cache first
            if (_accountIdCache.ContainsKey(login))
            {
                Console.WriteLine($"Using cached account: {_accountIdCache[login]}");
                return _accountIdCache[login];
            }

            // Check if account already exists on MetaApi
            string? existingId = await FindExistingAccountAsync(login);
            if (existingId != null)
            {
                Console.WriteLine($"Found existing account: {existingId}");
                _accountIdCache[login] = existingId;
                
                // Make sure it's deployed
                await EnsureDeployedAsync(existingId);
                
                return existingId;
            }

            // Create new account
            Console.WriteLine("Creating new MetaApi account...");
            string? newId = await CreateAccountAsync(login, password, server);
            
            if (newId != null)
            {
                _accountIdCache[login] = newId;
            }
            
            return newId;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"GetOrCreateAccount error: {ex.Message}");
            return null;
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // CREATE NEW ACCOUNT
    // ═══════════════════════════════════════════════════════════════
    private async Task<string?> CreateAccountAsync(string login, string password, string server)
    {
        try
        {
            var payload = new
            {
                name = $"ProfitX_{login}",
                type = "cloud",
                login = login,
                password = password,
                server = server,
                platform = "mt5",
                magic = 123456
            };

            string json = JsonConvert.SerializeObject(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync($"{_apiUrl}/users/current/accounts", content);
            string responseBody = await response.Content.ReadAsStringAsync();

            Console.WriteLine($"Create response: {response.StatusCode}");

            if (response.IsSuccessStatusCode)
            {
                var result = JObject.Parse(responseBody);
                string accountId = result["id"]?.ToString() ?? "";
                Console.WriteLine($"Account created: {accountId}");

                await DeployAccountAsync(accountId);
                return accountId;
            }
            else
            {
                Console.WriteLine($"Create error: {responseBody}");
                return null;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Create error: {ex.Message}");
            return null;
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // FIND EXISTING ACCOUNT
    // ═══════════════════════════════════════════════════════════════
    private async Task<string?> FindExistingAccountAsync(string login)
    {
        try
        {
            Console.WriteLine($"Searching for existing account: {login}");
            
            var response = await _httpClient.GetAsync($"{_apiUrl}/users/current/accounts");
            string responseBody = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                var accounts = JArray.Parse(responseBody);
                Console.WriteLine($"Found {accounts.Count} accounts on MetaApi");
                
                foreach (var account in accounts)
                {
                    string accLogin = account["login"]?.ToString() ?? "";
                    string accId = account["id"]?.ToString() ?? "";
                    string accState = account["state"]?.ToString() ?? "";
                    
                    Console.WriteLine($"  Account: {accLogin} -> {accId} ({accState})");
                    
                    if (accLogin == login)
                    {
                        Console.WriteLine($"  MATCH FOUND: {accId}");
                        return accId;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Find error: {ex.Message}");
        }

        return null;
    }

    // ═══════════════════════════════════════════════════════════════
    // DEPLOY ACCOUNT
    // ═══════════════════════════════════════════════════════════════
    private async Task DeployAccountAsync(string accountId)
    {
        try
        {
            Console.WriteLine($"Deploying account: {accountId}");
            
            var response = await _httpClient.PostAsync(
                $"{_apiUrl}/users/current/accounts/{accountId}/deploy",
                new StringContent("", Encoding.UTF8, "application/json"));

            Console.WriteLine($"Deploy response: {response.StatusCode}");
            
            // Wait for deployment
            Console.WriteLine("Waiting for deployment...");
            await Task.Delay(8000);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Deploy error: {ex.Message}");
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // ENSURE ACCOUNT IS DEPLOYED
    // ═══════════════════════════════════════════════════════════════
    private async Task EnsureDeployedAsync(string accountId)
    {
        try
        {
            var response = await _httpClient.GetAsync(
                $"{_apiUrl}/users/current/accounts/{accountId}");
            
            if (response.IsSuccessStatusCode)
            {
                string body = await response.Content.ReadAsStringAsync();
                var data = JObject.Parse(body);
                string state = data["state"]?.ToString() ?? "";
                
                Console.WriteLine($"Account state: {state}");
                
                if (state != "DEPLOYED")
                {
                    Console.WriteLine("Account not deployed, deploying now...");
                    await DeployAccountAsync(accountId);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"EnsureDeployed error: {ex.Message}");
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // GET ACCOUNT INFO
    // ═══════════════════════════════════════════════════════════════
    public async Task<AccountData?> GetAccountInfoAsync(string accountId)
    {
        try
        {
            Console.WriteLine($"Getting account info for: {accountId}");
            
            // Wait a moment for connection
            await Task.Delay(2000);
            
            var response = await _httpClient.GetAsync(
                $"https://mt-client-api-v1.agiliumtrade.agiliumtrade.ai/users/current/accounts/{accountId}/account-information");

            Console.WriteLine($"Account info response: {response.StatusCode}");

            if (response.IsSuccessStatusCode)
            {
                string responseBody = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"Account info: {responseBody.Substring(0, Math.Min(200, responseBody.Length))}");
                
                var data = JObject.Parse(responseBody);

                var accountData = new AccountData
                {
                    AccountNumber = data["login"]?.ToString() ?? "",
                    AccountName = data["name"]?.ToString() ?? "",
                    Server = data["server"]?.ToString() ?? "",
                    Currency = data["currency"]?.ToString() ?? "USD",
                    Leverage = data["leverage"]?.Value<int>() ?? 0,
                    Balance = data["balance"]?.Value<double>() ?? 0,
                    Equity = data["equity"]?.Value<double>() ?? 0,
                    Margin = data["margin"]?.Value<double>() ?? 0,
                    FreeMargin = data["freeMargin"]?.Value<double>() ?? 0,
                    MarginLevel = data["marginLevel"]?.Value<double>() ?? 0,
                    IsConnected = true,
                    LastUpdate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                };
                
                Console.WriteLine($"Balance: ${accountData.Balance}, Equity: ${accountData.Equity}");
                return accountData;
            }
            else
            {
                string errorBody = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"Account info error: {response.StatusCode} - {errorBody}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Account info error: {ex.Message}");
        }

        return null;
    }

    // ═══════════════════════════════════════════════════════════════
    // GET OPEN POSITIONS
    // ═══════════════════════════════════════════════════════════════
    public async Task<List<TradeData>> GetOpenPositionsAsync(string accountId)
    {
        var trades = new List<TradeData>();

        try
        {
            var response = await _httpClient.GetAsync(
                $"https://mt-client-api-v1.agiliumtrade.agiliumtrade.ai/users/current/accounts/{accountId}/positions");

            if (response.IsSuccessStatusCode)
            {
                string responseBody = await response.Content.ReadAsStringAsync();
                var positions = JArray.Parse(responseBody);

                foreach (var pos in positions)
                {
                    trades.Add(new TradeData
                    {
                        Ticket = pos["id"]?.Value<long>() ?? 0,
                        Type = pos["type"]?.ToString()?.ToUpper() ?? "",
                        Symbol = pos["symbol"]?.ToString() ?? "",
                        LotSize = pos["volume"]?.Value<double>() ?? 0,
                        EntryPrice = pos["openPrice"]?.Value<double>() ?? 0,
                        CurrentPrice = pos["currentPrice"]?.Value<double>() ?? 0,
                        StopLoss = pos["stopLoss"]?.Value<double>() ?? 0,
                        TakeProfit = pos["takeProfit"]?.Value<double>() ?? 0,
                        Profit = pos["profit"]?.Value<double>() ?? 0,
                        Swap = pos["swap"]?.Value<double>() ?? 0,
                        OpenTime = pos["time"]?.ToString() ?? "",
                        IsOpen = true
                    });
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Positions error: {ex.Message}");
        }

        return trades;
    }

    // ═══════════════════════════════════════════════════════════════
    // PLACE BUY ORDER
    // ═══════════════════════════════════════════════════════════════
    public async Task<bool> PlaceBuyOrderAsync(string accountId, string symbol, double lots, double sl, double tp, string comment)
    {
        try
        {
            var payload = new
            {
                actionType = "ORDER_TYPE_BUY",
                symbol = symbol,
                volume = lots,
                stopLoss = sl,
                takeProfit = tp,
                comment = comment
            };

            string json = JsonConvert.SerializeObject(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(
                $"https://mt-client-api-v1.agiliumtrade.agiliumtrade.ai/users/current/accounts/{accountId}/trade", content);

            string responseBody = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"BUY: {response.StatusCode} - {responseBody}");

            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"BUY error: {ex.Message}");
            return false;
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // PLACE SELL ORDER
    // ═══════════════════════════════════════════════════════════════
    public async Task<bool> PlaceSellOrderAsync(string accountId, string symbol, double lots, double sl, double tp, string comment)
    {
        try
        {
            var payload = new
            {
                actionType = "ORDER_TYPE_SELL",
                symbol = symbol,
                volume = lots,
                stopLoss = sl,
                takeProfit = tp,
                comment = comment
            };

            string json = JsonConvert.SerializeObject(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(
                $"https://mt-client-api-v1.agiliumtrade.agiliumtrade.ai/users/current/accounts/{accountId}/trade", content);

            string responseBody = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"SELL: {response.StatusCode} - {responseBody}");

            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"SELL error: {ex.Message}");
            return false;
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // CLOSE POSITION
    // ═══════════════════════════════════════════════════════════════
    public async Task<bool> ClosePositionAsync(string accountId, string positionId)
    {
        try
        {
            var payload = new
            {
                actionType = "POSITION_CLOSE_ID",
                positionId = positionId
            };

            string json = JsonConvert.SerializeObject(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(
                $"https://mt-client-api-v1.agiliumtrade.agiliumtrade.ai/users/current/accounts/{accountId}/trade", content);

            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Close error: {ex.Message}");
            return false;
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // GET CANDLES FOR STRATEGY
    // ═══════════════════════════════════════════════════════════════
    public async Task<List<CandleData>> GetCandlesAsync(string accountId, string symbol, string timeframe, int count)
    {
        var candles = new List<CandleData>();

        try
        {
            var response = await _httpClient.GetAsync(
                $"https://mt-client-api-v1.agiliumtrade.agiliumtrade.ai/users/current/accounts/{accountId}/historical-market-data/symbols/{symbol}/timeframes/{timeframe}/candles?limit={count}");

            if (response.IsSuccessStatusCode)
            {
                string responseBody = await response.Content.ReadAsStringAsync();
                var data = JArray.Parse(responseBody);

                foreach (var candle in data)
                {
                    candles.Add(new CandleData
                    {
                        Time = candle["time"]?.ToString() ?? "",
                        Open = candle["open"]?.Value<double>() ?? 0,
                        High = candle["high"]?.Value<double>() ?? 0,
                        Low = candle["low"]?.Value<double>() ?? 0,
                        Close = candle["close"]?.Value<double>() ?? 0,
                        Volume = candle["tickVolume"]?.Value<long>() ?? 0
                    });
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Candles error: {ex.Message}");
        }

        return candles;
    }

    // ═══════════════════════════════════════════════════════════════
    // GET CURRENT PRICE
    // ═══════════════════════════════════════════════════════════════
    public async Task<(double bid, double ask)> GetCurrentPriceAsync(string accountId, string symbol)
    {
        try
        {
            var response = await _httpClient.GetAsync(
                $"https://mt-client-api-v1.agiliumtrade.agiliumtrade.ai/users/current/accounts/{accountId}/symbols/{symbol}/current-price");

            if (response.IsSuccessStatusCode)
            {
                string responseBody = await response.Content.ReadAsStringAsync();
                var data = JObject.Parse(responseBody);

                return (data["bid"]?.Value<double>() ?? 0, data["ask"]?.Value<double>() ?? 0);
            }
        }
        catch { }

        return (0, 0);
    }
}

public class CandleData
{
    public string Time { get; set; } = "";
    public double Open { get; set; }
    public double High { get; set; }
    public double Low { get; set; }
    public double Close { get; set; }
    public long Volume { get; set; }
}
