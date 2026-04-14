using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public class MetaApiHelper
{
    private static readonly Dictionary<string, HttpClient> _clients = new();

    private static HttpClient GetClient(string token)
    {
        if (!_clients.ContainsKey(token))
        {
            var client = new HttpClient();
            client.DefaultRequestHeaders.Add("auth-token", token);
            client.Timeout = TimeSpan.FromSeconds(30);
            _clients[token] = client;
        }
        return _clients[token];
    }

    private static string API = "https://mt-provisioning-api-v1.agiliumtrade.agiliumtrade.ai";
    private static string TRADE = "https://mt-client-api-v1.agiliumtrade.agiliumtrade.ai";

    public static async Task<string?> ConnectAccount(string token, string login, string password, string server)
    {
        var http = GetClient(token);

        try
        {
            // Check existing accounts
            Console.WriteLine($"Checking existing accounts for {login}...");
            var listResponse = await http.GetAsync($"{API}/users/current/accounts");

            if (listResponse.IsSuccessStatusCode)
            {
                string listBody = await listResponse.Content.ReadAsStringAsync();
                var accounts = JArray.Parse(listBody);

                foreach (var acc in accounts)
                {
                    string accLogin = acc["login"]?.ToString() ?? "";
                    string accId = acc["_id"]?.ToString() ?? "";
                    string accState = acc["state"]?.ToString() ?? "";

                    if (accLogin == login && !string.IsNullOrEmpty(accId))
                    {
                        Console.WriteLine($"Found existing: {accId} State:{accState}");

                        if (accState != "DEPLOYED")
                        {
                            Console.WriteLine("Deploying...");
                            await http.PostAsync($"{API}/users/current/accounts/{accId}/deploy",
                                new StringContent("", Encoding.UTF8, "application/json"));
                            await WaitForDeployment(http, accId);
                        }

                        return accId;
                    }
                }
            }

            // Create new account
            Console.WriteLine($"Creating new account for {login}...");

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
            var response = await http.PostAsync($"{API}/users/current/accounts", content);
            string body = await response.Content.ReadAsStringAsync();

            Console.WriteLine($"Create: {response.StatusCode} - {body}");

            if (response.IsSuccessStatusCode)
            {
                var result = JObject.Parse(body);
                string accountId = result["_id"]?.ToString() ?? "";

                if (!string.IsNullOrEmpty(accountId))
                {
                    Console.WriteLine($"Created: {accountId}");

                    // Deploy
                    await http.PostAsync($"{API}/users/current/accounts/{accountId}/deploy",
                        new StringContent("", Encoding.UTF8, "application/json"));

                    await WaitForDeployment(http, accountId);

                    return accountId;
                }
            }

            Console.WriteLine($"Create failed: {body}");
            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ConnectAccount error: {ex.Message}");
            return null;
        }
    }

    private static async Task WaitForDeployment(HttpClient http, string accountId)
    {
        Console.WriteLine("Waiting for deployment...");

        for (int i = 0; i < 8; i++)
        {
            await Task.Delay(5000);

            try
            {
                var response = await http.GetAsync($"{API}/users/current/accounts/{accountId}");
                if (response.IsSuccessStatusCode)
                {
                    string body = await response.Content.ReadAsStringAsync();
                    var data = JObject.Parse(body);
                    string state = data["state"]?.ToString() ?? "";
                    string conn = data["connectionStatus"]?.ToString() ?? "";

                    Console.WriteLine($"  State:{state} Connection:{conn}");

                    if (state == "DEPLOYED" && conn == "CONNECTED")
                    {
                        Console.WriteLine("  READY!");
                        return;
                    }
                }
            }
            catch { }
        }

        Console.WriteLine("  Deploy timeout - will retry later");
    }

    public static async Task<AccountData?> GetAccountInfo(string token, string accountId)
    {
        if (string.IsNullOrEmpty(accountId)) return null;

        try
        {
            var http = GetClient(token);
            var response = await http.GetAsync(
                $"{TRADE}/users/current/accounts/{accountId}/account-information");

            if (response.IsSuccessStatusCode)
            {
                string body = await response.Content.ReadAsStringAsync();
                var d = JObject.Parse(body);

                return new AccountData
                {
                    AccountNumber = d["login"]?.ToString() ?? "",
                    AccountName = d["name"]?.ToString() ?? "",
                    Server = d["server"]?.ToString() ?? "",
                    Currency = d["currency"]?.ToString() ?? "USD",
                    Leverage = d["leverage"]?.Value<int>() ?? 0,
                    Balance = d["balance"]?.Value<double>() ?? 0,
                    Equity = d["equity"]?.Value<double>() ?? 0,
                    Margin = d["margin"]?.Value<double>() ?? 0,
                    FreeMargin = d["freeMargin"]?.Value<double>() ?? 0,
                    MarginLevel = d["marginLevel"]?.Value<double>() ?? 0,
                    IsConnected = true,
                    LastUpdate = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")
                };
            }
            else
            {
                string err = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"AccountInfo error: {response.StatusCode} - {err}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"AccountInfo error: {ex.Message}");
        }

        return null;
    }

    public static async Task<List<TradeData>> GetPositions(string token, string accountId)
    {
        var trades = new List<TradeData>();
        if (string.IsNullOrEmpty(accountId)) return trades;

        try
        {
            var http = GetClient(token);
            var response = await http.GetAsync(
                $"{TRADE}/users/current/accounts/{accountId}/positions");

            if (response.IsSuccessStatusCode)
            {
                string body = await response.Content.ReadAsStringAsync();
                var positions = JArray.Parse(body);

                foreach (var p in positions)
                {
                    trades.Add(new TradeData
                    {
                        Ticket = p["id"]?.Value<long>() ?? 0,
                        Type = p["type"]?.ToString()?.ToUpper() ?? "",
                        Symbol = p["symbol"]?.ToString() ?? "",
                        LotSize = p["volume"]?.Value<double>() ?? 0,
                        EntryPrice = p["openPrice"]?.Value<double>() ?? 0,
                        CurrentPrice = p["currentPrice"]?.Value<double>() ?? 0,
                        StopLoss = p["stopLoss"]?.Value<double>() ?? 0,
                        TakeProfit = p["takeProfit"]?.Value<double>() ?? 0,
                        Profit = p["profit"]?.Value<double>() ?? 0,
                        Swap = p["swap"]?.Value<double>() ?? 0,
                        OpenTime = p["time"]?.ToString() ?? "",
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

    public static async Task<List<CandleData>> GetCandles(string token, string accountId, string symbol, string timeframe, int count)
    {
        var candles = new List<CandleData>();
        if (string.IsNullOrEmpty(accountId)) return candles;

        try
        {
            var http = GetClient(token);
            var response = await http.GetAsync(
                $"{TRADE}/users/current/accounts/{accountId}/historical-market-data/symbols/{symbol}/timeframes/{timeframe}/candles?limit={count}");

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

    public static async Task<(double bid, double ask)> GetPrice(string token, string accountId, string symbol)
    {
        if (string.IsNullOrEmpty(accountId)) return (0, 0);

        try
        {
            var http = GetClient(token);
            var response = await http.GetAsync(
                $"{TRADE}/users/current/accounts/{accountId}/symbols/{symbol}/current-price");

            if (response.IsSuccessStatusCode)
            {
                string body = await response.Content.ReadAsStringAsync();
                var d = JObject.Parse(body);
                return (d["bid"]?.Value<double>() ?? 0, d["ask"]?.Value<double>() ?? 0);
            }
        }
        catch { }

        return (0, 0);
    }

    public static async Task<bool> PlaceTrade(string token, string accountId, string actionType, string symbol, double lots, double sl, double tp, string comment)
    {
        if (string.IsNullOrEmpty(accountId)) return false;

        try
        {
            var http = GetClient(token);

            var payload = new
            {
                actionType = actionType,
                symbol = symbol,
                volume = lots,
                stopLoss = sl,
                takeProfit = tp,
                comment = comment
            };

            var response = await http.PostAsync(
                $"{TRADE}/users/current/accounts/{accountId}/trade",
                new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json"));

            string body = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"Trade {actionType}: {response.StatusCode} - {body}");

            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Trade error: {ex.Message}");
            return false;
        }
    }
}
