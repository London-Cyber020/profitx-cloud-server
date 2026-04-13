using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public class MetaApiService
{
    private readonly HttpClient _httpClient;
    private readonly string _apiToken;
    private readonly string _apiUrl = "https://mt-provisioning-api-v1.agiliumtrade.agiliumtrade.ai";
    private readonly string _tradingUrl = "https://mt-client-api-v1.agiliumtrade.agiliumtrade.ai";
    private readonly DataStore _store;
    private readonly Dictionary<string, string> _accountIdCache = new();

    public MetaApiService(DataStore store)
    {
        _store = store;
        _apiToken = Environment.GetEnvironmentVariable("METAAPI_TOKEN") ?? "";

        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("auth-token", _apiToken);
        _httpClient.Timeout = TimeSpan.FromSeconds(30);

        Console.WriteLine("MetaApi initialized. Token: " +
            (_apiToken.Length > 10 ? _apiToken.Substring(0, 10) + "..." : "NOT SET"));
    }

    public async Task<string?> GetOrCreateAccountAsync(string login, string password, string server)
    {
        try
        {
            // Check cache
            if (_accountIdCache.ContainsKey(login) && !string.IsNullOrEmpty(_accountIdCache[login]))
            {
                Console.WriteLine($"Cache hit: {_accountIdCache[login]}");
                await EnsureDeployedAsync(_accountIdCache[login]);
                return _accountIdCache[login];
            }

            // Find existing
            string? existingId = await FindExistingAccountAsync(login);
            if (!string.IsNullOrEmpty(existingId))
            {
                Console.WriteLine($"Found existing: {existingId}");
                _accountIdCache[login] = existingId;
                await EnsureDeployedAsync(existingId);
                return existingId;
            }

            // Delete any broken accounts first
            await DeleteBrokenAccountsAsync(login);

            // Create new
            Console.WriteLine("Creating new account...");
            return await CreateNewAccountAsync(login, password, server);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"GetOrCreate error: {ex.Message}");
            return null;
        }
    }

    private async Task<string?> FindExistingAccountAsync(string login)
    {
        try
        {
            var response = await _httpClient.GetAsync($"{_apiUrl}/users/current/accounts");
            if (!response.IsSuccessStatusCode) return null;

            string body = await response.Content.ReadAsStringAsync();
            var accounts = JArray.Parse(body);

            Console.WriteLine($"Found {accounts.Count} MetaApi accounts");

            foreach (var acc in accounts)
            {
                string accLogin = acc["login"]?.ToString() ?? "";
                string accId = acc["_id"]?.ToString() ?? acc["id"]?.ToString() ?? "";
                string accState = acc["state"]?.ToString() ?? "";

                Console.WriteLine($"  {accLogin} -> ID:{accId} State:{accState}");

                if (accLogin == login && !string.IsNullOrEmpty(accId))
                {
                    return accId;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Find error: {ex.Message}");
        }

        return null;
    }

    private async Task DeleteBrokenAccountsAsync(string login)
    {
        try
        {
            var response = await _httpClient.GetAsync($"{_apiUrl}/users/current/accounts");
            if (!response.IsSuccessStatusCode) return;

            string body = await response.Content.ReadAsStringAsync();
            var accounts = JArray.Parse(body);

            foreach (var acc in accounts)
            {
                string accLogin = acc["login"]?.ToString() ?? "";
                string accId = acc["_id"]?.ToString() ?? acc["id"]?.ToString() ?? "";

                if (accLogin == login && !string.IsNullOrEmpty(accId))
                {
                    Console.WriteLine($"Deleting broken account: {accId}");

                    // Undeploy first
                    await _httpClient.PostAsync(
                        $"{_apiUrl}/users/current/accounts/{accId}/undeploy",
                        new StringContent("", Encoding.UTF8, "application/json"));

                    await Task.Delay(2000);

                    // Delete
                    await _httpClient.DeleteAsync(
                        $"{_apiUrl}/users/current/accounts/{accId}");

                    Console.WriteLine($"Deleted: {accId}");
                    await Task.Delay(2000);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Delete error: {ex.Message}");
        }
    }

    private async Task<string?> CreateNewAccountAsync(string login, string password, string server)
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

            var response = await _httpClient.PostAsync(
                $"{_apiUrl}/users/current/accounts", content);

            string responseBody = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"Create response: {response.StatusCode}");
            Console.WriteLine($"Create body: {responseBody}");

            if (response.IsSuccessStatusCode)
            {
                var result = JObject.Parse(responseBody);
                string accountId = result["_id"]?.ToString() ?? result["id"]?.ToString() ?? "";

                if (!string.IsNullOrEmpty(accountId))
                {
                    Console.WriteLine($"Created account: {accountId}");
                    _accountIdCache[login] = accountId;

                    // Deploy
                    await DeployAsync(accountId);

                    return accountId;
                }
            }

            Console.WriteLine($"Create failed: {responseBody}");
            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Create error: {ex.Message}");
            return null;
        }
    }

    private async Task DeployAsync(string accountId)
    {
        try
        {
            Console.WriteLine($"Deploying: {accountId}");

            var response = await _httpClient.PostAsync(
                $"{_apiUrl}/users/current/accounts/{accountId}/deploy",
                new StringContent("", Encoding.UTF8, "application/json"));

            Console.WriteLine($"Deploy: {response.StatusCode}");

            // Wait for deployment
            for (int i = 0; i < 6; i++)
            {
                await Task.Delay(5000);

                var checkResponse = await _httpClient.GetAsync(
                    $"{_apiUrl}/users/current/accounts/{accountId}");

                if (checkResponse.IsSuccessStatusCode)
                {
                    string body = await checkResponse.Content.ReadAsStringAsync();
                    var data = JObject.Parse(body);
                    string state = data["state"]?.ToString() ?? "";
                    string connStatus = data["connectionStatus"]?.ToString() ?? "";

                    Console.WriteLine($"  State: {state}, Connection: {connStatus}");

                    if (state == "DEPLOYED" && connStatus == "CONNECTED")
                    {
                        Console.WriteLine("  Account READY!");
                        return;
                    }
                }
            }

            Console.WriteLine("  Deploy timeout - will retry on next request");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Deploy error: {ex.Message}");
        }
    }

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
                    await DeployAsync(accountId);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"EnsureDeployed error: {ex.Message}");
        }
    }

    public async Task<AccountData?> GetAccountInfoAsync(string accountId)
    {
        if (string.IsNullOrEmpty(accountId))
        {
            Console.WriteLine("GetAccountInfo: empty accountId!");
            return null;
        }

        try
        {
            Console.WriteLine($"Getting account info: {accountId}");

            var response = await _httpClient.GetAsync(
                $"{_tradingUrl}/users/current/accounts/{accountId}/account-information");

            Console.WriteLine($"Account info status: {response.StatusCode}");

            if (response.IsSuccessStatusCode)
            {
                string body = await response.Content.ReadAsStringAsync();
                var data = JObject.Parse(body);

                var result = new AccountData
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

                Console.WriteLine($"Balance: ${result.Balance} Equity: ${result.Equity}");
                return result;
            }
            else
            {
                string errorBody = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"Account info error: {errorBody}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Account info error: {ex.Message}");
        }

        return null;
    }

    public async Task<List<TradeData>> GetOpenPositionsAsync(string accountId)
    {
        var trades = new List<TradeData>();
        if (string.IsNullOrEmpty(accountId)) return trades;

        try
        {
            var response = await _httpClient.GetAsync(
                $"{_tradingUrl}/users/current/accounts/{accountId}/positions");

            if (response.IsSuccessStatusCode)
            {
                string body = await response.Content.ReadAsStringAsync();
                var positions = JArray.Parse(body);

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

    public async Task<bool> PlaceBuyOrderAsync(string accountId, string symbol,
        double lots, double sl, double tp, string comment)
    {
        if (string.IsNullOrEmpty(accountId)) return false;

        try
        {
            var payload = new
            {
                actionType = "ORDER_TYPE_BUY",
                symbol, volume = lots,
                stopLoss = sl, takeProfit = tp, comment
            };

            var response = await _httpClient.PostAsync(
                $"{_tradingUrl}/users/current/accounts/{accountId}/trade",
                new StringContent(JsonConvert.SerializeObject(payload),
                    Encoding.UTF8, "application/json"));

            Console.WriteLine($"BUY: {response.StatusCode}");
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"BUY error: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> PlaceSellOrderAsync(string accountId, string symbol,
        double lots, double sl, double tp, string comment)
    {
        if (string.IsNullOrEmpty(accountId)) return false;

        try
        {
            var payload = new
            {
                actionType = "ORDER_TYPE_SELL",
                symbol, volume = lots,
                stopLoss = sl, takeProfit = tp, comment
            };

            var response = await _httpClient.PostAsync(
                $"{_tradingUrl}/users/current/accounts/{accountId}/trade",
                new StringContent(JsonConvert.SerializeObject(payload),
                    Encoding.UTF8, "application/json"));

            Console.WriteLine($"SELL: {response.StatusCode}");
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"SELL error: {ex.Message}");
            return false;
        }
    }

    public async Task<(double bid, double ask)> GetCurrentPriceAsync(
        string accountId, string symbol)
    {
        if (string.IsNullOrEmpty(accountId)) return (0, 0);

        try
        {
            var response = await _httpClient.GetAsync(
                $"{_tradingUrl}/users/current/accounts/{accountId}/symbols/{symbol}/current-price");

            if (response.IsSuccessStatusCode)
            {
                string body = await response.Content.ReadAsStringAsync();
                var data = JObject.Parse(body);
                return (data["bid"]?.Value<double>() ?? 0, data["ask"]?.Value<double>() ?? 0);
            }
        }
        catch { }

        return (0, 0);
    }

    public async Task<List<CandleData>> GetCandlesAsync(
        string accountId, string symbol, string timeframe, int count)
    {
        var candles = new List<CandleData>();
        if (string.IsNullOrEmpty(accountId)) return candles;

        try
        {
            var response = await _httpClient.GetAsync(
                $"{_tradingUrl}/users/current/accounts/{accountId}/historical-market-data/symbols/{symbol}/timeframes/{timeframe}/candles?limit={count}");

            if (response.IsSuccessStatusCode)
            {
                string body = await response.Content.ReadAsStringAsync();
                var data = JArray.Parse(body);

                foreach (var c in data)
                {
                    candles.Add(new CandleData
                    {
                        Time = c["time"]?.ToString() ?? "",
                        Open = c["open"]?.Value<double>() ?? 0,
                        High = c["high"]?.Value<double>() ?? 0,
                        Low = c["low"]?.Value<double>() ?? 0,
                        Close = c["close"]?.Value<double>() ?? 0,
                        Volume = c["tickVolume"]?.Value<long>() ?? 0
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
