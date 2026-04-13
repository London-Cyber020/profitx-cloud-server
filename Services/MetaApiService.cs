using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public class MetaApiService
{
    private readonly HttpClient _httpClient;
    private readonly string _apiToken;
    private readonly string _apiUrl = "https://mt-provisioning-api-v1.agiliumtrade.agiliumtrade.ai";
    private readonly string _tradingApiUrl = "https://mt-client-api-v1.agiliumtrade.agiliumtrade.ai";
    private readonly DataStore _store;

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
    // CREATE METAAPI ACCOUNT (Connect MT5 to MetaApi)
    // ═══════════════════════════════════════════════════════════════
    public async Task<string?> CreateAccountAsync(string login, string password, string server)
    {
        try
        {
            Console.WriteLine($"Creating MetaApi account for login: {login}");

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

            Console.WriteLine($"MetaApi response: {response.StatusCode}");

            if (response.IsSuccessStatusCode)
            {
                var result = JObject.Parse(responseBody);
                string accountId = result["id"]?.ToString() ?? "";
                Console.WriteLine($"MetaApi account created: {accountId}");

                // Deploy the account
                await DeployAccountAsync(accountId);

                return accountId;
            }
            else
            {
                // Check if account already exists
                var existingId = await FindExistingAccountAsync(login);
                if (existingId != null)
                {
                    Console.WriteLine($"Using existing account: {existingId}");
                    return existingId;
                }

                Console.WriteLine($"MetaApi error: {responseBody}");
                return null;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"MetaApi error: {ex.Message}");
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
            var response = await _httpClient.GetAsync($"{_apiUrl}/users/current/accounts");
            string responseBody = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                var accounts = JArray.Parse(responseBody);
                foreach (var account in accounts)
                {
                    if (account["login"]?.ToString() == login)
                    {
                        return account["id"]?.ToString();
                    }
                }
            }
        }
        catch { }

        return null;
    }

    // ═══════════════════════════════════════════════════════════════
    // DEPLOY ACCOUNT
    // ═══════════════════════════════════════════════════════════════
    private async Task DeployAccountAsync(string accountId)
    {
        try
        {
            var response = await _httpClient.PostAsync(
                $"{_apiUrl}/users/current/accounts/{accountId}/deploy",
                new StringContent("", Encoding.UTF8, "application/json"));

            Console.WriteLine($"Deploy response: {response.StatusCode}");

            // Wait for deployment
            await Task.Delay(5000);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Deploy error: {ex.Message}");
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // GET ACCOUNT INFO
    // ═══════════════════════════════════════════════════════════════
    public async Task<AccountData?> GetAccountInfoAsync(string accountId)
    {
        try
        {
            var response = await _httpClient.GetAsync(
                $"{_tradingApiUrl}/users/current/accounts/{accountId}/account-information");

            if (response.IsSuccessStatusCode)
            {
                string responseBody = await response.Content.ReadAsStringAsync();
                var data = JObject.Parse(responseBody);

                return new AccountData
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
                $"{_tradingApiUrl}/users/current/accounts/{accountId}/positions");

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
    // GET PRICE DATA (Candles)
    // ═══════════════════════════════════════════════════════════════
    public async Task<List<CandleData>> GetCandlesAsync(string accountId, string symbol, string timeframe, int count)
    {
        var candles = new List<CandleData>();

        try
        {
            var response = await _httpClient.GetAsync(
                $"{_tradingApiUrl}/users/current/accounts/{accountId}/historical-market-data/symbols/{symbol}/timeframes/{timeframe}/candles?limit={count}");

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
                $"{_tradingApiUrl}/users/current/accounts/{accountId}/symbols/{symbol}/current-price");

            if (response.IsSuccessStatusCode)
            {
                string responseBody = await response.Content.ReadAsStringAsync();
                var data = JObject.Parse(responseBody);

                double bid = data["bid"]?.Value<double>() ?? 0;
                double ask = data["ask"]?.Value<double>() ?? 0;

                return (bid, ask);
            }
        }
        catch { }

        return (0, 0);
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
                $"{_tradingApiUrl}/users/current/accounts/{accountId}/trade", content);

            string responseBody = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"BUY order: {response.StatusCode} - {responseBody}");

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
                $"{_tradingApiUrl}/users/current/accounts/{accountId}/trade", content);

            string responseBody = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"SELL order: {response.StatusCode} - {responseBody}");

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
                $"{_tradingApiUrl}/users/current/accounts/{accountId}/trade", content);

            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Close error: {ex.Message}");
            return false;
        }
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
