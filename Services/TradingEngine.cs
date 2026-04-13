public class TradingEngine : BackgroundService
{
    private readonly DataStore _store;
    private readonly MetaApiService _metaApi;
    private readonly Dictionary<string, string> _accountIds = new();

    public TradingEngine(DataStore store, MetaApiService metaApi)
    {
        _store = store;
        _metaApi = metaApi;
        Console.WriteLine("Trading Engine initialized");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Console.WriteLine("Trading Engine started - checking every 30 seconds");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                foreach (var bot in _store.ActiveBots)
                {
                    if (bot.Value.IsRunning)
                    {
                        await ProcessBotAsync(bot.Key, bot.Value);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Engine error: {ex.Message}");
            }

            await Task.Delay(30000, stoppingToken); // Check every 30 seconds
        }
    }

    private async Task ProcessBotAsync(string key, BotSession bot)
    {
        try
        {
            // Get MetaApi account ID
            if (!_accountIds.ContainsKey(key))
            {
                Console.WriteLine($"No MetaApi account for: {key}");
                return;
            }

            string accountId = _accountIds[key];

            // Update account info
            var accountInfo = await _metaApi.GetAccountInfoAsync(accountId);
            if (accountInfo != null)
            {
                accountInfo.OpenTrades = (await _metaApi.GetOpenPositionsAsync(accountId)).Count;
                _store.AccountsData[key] = accountInfo;
            }

            // Update open trades
            var openTrades = await _metaApi.GetOpenPositionsAsync(accountId);
            _store.OpenTrades[key] = openTrades;

            // Check if we should trade
            if (!IsValidTradingSession())
            {
                bot.Status = "Waiting for session";
                return;
            }

            // Check max trades
            if (openTrades.Count >= bot.MaxTrades)
            {
                bot.Status = $"Max trades reached ({openTrades.Count}/{bot.MaxTrades})";
                return;
            }

            bot.Status = "Analyzing market...";

            // Get candles for analysis
            string timeframe = "15m";
            var candles = await _metaApi.GetCandlesAsync(accountId, bot.Symbol, timeframe, 100);

            if (candles.Count < 20)
            {
                bot.Status = "Not enough data";
                return;
            }

            // Run strategy
            if (bot.Strategy == "ICT")
            {
                await RunICTStrategy(accountId, bot, candles);
            }
            else if (bot.Strategy == "SMC")
            {
                await RunSMCStrategy(accountId, bot, candles);
            }

            bot.Status = $"Running on {bot.Symbol} ({bot.Strategy})";
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Bot processing error: {ex.Message}");
            bot.Status = $"Error: {ex.Message}";
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // ICT STRATEGY
    // ═══════════════════════════════════════════════════════════════
    private async Task RunICTStrategy(string accountId, BotSession bot, List<CandleData> candles)
    {
        try
        {
            // Get current price
            var (bid, ask) = await _metaApi.GetCurrentPriceAsync(accountId, bot.Symbol);
            if (bid == 0 || ask == 0) return;

            // Check trend (last 50 candles)
            string trend = GetTrend(candles);

            // Find Order Block
            int obIndex = FindBullishOrderBlock(candles);
            int bearishOB = FindBearishOrderBlock(candles);

            double point = GetPointSize(bot.Symbol);
            if (point <= 0) return;

            // BUY setup
            if (trend == "BULLISH" && obIndex > 0)
            {
                double obLow = candles[obIndex].Low;
                double obHigh = candles[obIndex].High;

                if (ask >= obLow && ask <= obHigh)
                {
                    if (HasBullishFVG(candles) && HasBullishMomentum(candles))
                    {
                        double sl = obLow - 10 * point;
                        double slPips = (ask - sl) / point;
                        double tp = ask + slPips * 1.2 * point;

                        Console.WriteLine($"ICT BUY Signal: {bot.Symbol} @ {ask}");
                        bool success = await _metaApi.PlaceBuyOrderAsync(
                            accountId, bot.Symbol, bot.LotSize, sl, tp, "ICT-BUY");

                        if (success)
                            Console.WriteLine("ICT BUY order placed!");
                    }
                }
            }

            // SELL setup
            if (trend == "BEARISH" && bearishOB > 0)
            {
                double obLow = candles[bearishOB].Low;
                double obHigh = candles[bearishOB].High;

                if (bid >= obLow && bid <= obHigh)
                {
                    if (HasBearishFVG(candles) && HasBearishMomentum(candles))
                    {
                        double sl = obHigh + 10 * point;
                        double slPips = (sl - bid) / point;
                        double tp = bid - slPips * 1.2 * point;

                        Console.WriteLine($"ICT SELL Signal: {bot.Symbol} @ {bid}");
                        bool success = await _metaApi.PlaceSellOrderAsync(
                            accountId, bot.Symbol, bot.LotSize, sl, tp, "ICT-SELL");

                        if (success)
                            Console.WriteLine("ICT SELL order placed!");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ICT Strategy error: {ex.Message}");
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // SMC STRATEGY
    // ═══════════════════════════════════════════════════════════════
    private async Task RunSMCStrategy(string accountId, BotSession bot, List<CandleData> candles)
    {
        try
        {
            var (bid, ask) = await _metaApi.GetCurrentPriceAsync(accountId, bot.Symbol);
            if (bid == 0 || ask == 0) return;

            string trend = GetTrend(candles);
            double point = GetPointSize(bot.Symbol);
            if (point <= 0) return;

            // Find swing high/low
            double swingHigh = 0, swingLow = 999999;
            for (int i = 0; i < Math.Min(50, candles.Count); i++)
            {
                if (candles[i].High > swingHigh) swingHigh = candles[i].High;
                if (candles[i].Low < swingLow) swingLow = candles[i].Low;
            }

            // BUY: Bullish BOS
            if (trend == "BULLISH")
            {
                double bosPips = (candles[0].High - swingHigh) / point;
                if (bosPips > 15)
                {
                    double sl = swingLow - 10 * point;
                    double slPips = (ask - sl) / point;
                    double tp = ask + slPips * 1.2 * point;

                    Console.WriteLine($"SMC BUY Signal: {bot.Symbol} @ {ask}");
                    await _metaApi.PlaceBuyOrderAsync(
                        accountId, bot.Symbol, bot.LotSize, sl, tp, "SMC-BUY");
                }
            }

            // SELL: Bearish BOS
            if (trend == "BEARISH")
            {
                double bosPips = (swingLow - candles[0].Low) / point;
                if (bosPips > 15)
                {
                    double sl = swingHigh + 10 * point;
                    double slPips = (sl - bid) / point;
                    double tp = bid - slPips * 1.2 * point;

                    Console.WriteLine($"SMC SELL Signal: {bot.Symbol} @ {bid}");
                    await _metaApi.PlaceSellOrderAsync(
                        accountId, bot.Symbol, bot.LotSize, sl, tp, "SMC-SELL");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"SMC Strategy error: {ex.Message}");
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // HELPER FUNCTIONS
    // ═══════════════════════════════════════════════════════════════
    private string GetTrend(List<CandleData> candles)
    {
        if (candles.Count < 10) return "UNKNOWN";

        double sum5 = 0, sum20 = 0;
        for (int i = 0; i < 5; i++) sum5 += candles[i].Close;
        for (int i = 0; i < 20 && i < candles.Count; i++) sum20 += candles[i].Close;

        double ma5 = sum5 / 5;
        double ma20 = sum20 / Math.Min(20, candles.Count);

        if (candles[0].Close > ma5 && ma5 > ma20) return "BULLISH";
        if (candles[0].Close < ma5 && ma5 < ma20) return "BEARISH";
        return "RANGING";
    }

    private int FindBullishOrderBlock(List<CandleData> candles)
    {
        int consecutiveBearish = 0;
        for (int i = 2; i < Math.Min(50, candles.Count); i++)
        {
            if (candles[i].Close < candles[i].Open)
            {
                consecutiveBearish++;
                if (consecutiveBearish >= 3)
                {
                    if (candles[1].Close > candles[i].High)
                        return i;
                }
            }
            else consecutiveBearish = 0;
        }
        return 0;
    }

    private int FindBearishOrderBlock(List<CandleData> candles)
    {
        int consecutiveBullish = 0;
        for (int i = 2; i < Math.Min(50, candles.Count); i++)
        {
            if (candles[i].Close > candles[i].Open)
            {
                consecutiveBullish++;
                if (consecutiveBullish >= 3)
                {
                    if (candles[1].Close < candles[i].Low)
                        return i;
                }
            }
            else consecutiveBullish = 0;
        }
        return 0;
    }

    private bool HasBullishFVG(List<CandleData> candles)
    {
        if (candles.Count < 4) return false;
        double gap = candles[1].Low - candles[3].High;
        return gap > 0;
    }

    private bool HasBearishFVG(List<CandleData> candles)
    {
        if (candles.Count < 4) return false;
        double gap = candles[3].Low - candles[1].High;
        return gap > 0;
    }

    private bool HasBullishMomentum(List<CandleData> candles)
    {
        if (candles.Count < 3) return false;
        return candles[0].Close > candles[1].Close && candles[1].Close > candles[2].Close;
    }

    private bool HasBearishMomentum(List<CandleData> candles)
    {
        if (candles.Count < 3) return false;
        return candles[0].Close < candles[1].Close && candles[1].Close < candles[2].Close;
    }

    private double GetPointSize(string symbol)
    {
        string upper = symbol.ToUpper();
        if (upper.Contains("JPY")) return 0.001;
        if (upper.Contains("XAU") || upper.Contains("GOLD")) return 0.01;
        if (upper.Contains("BTC")) return 1.0;
        if (upper.Contains("US30") || upper.Contains("US100")) return 0.1;
        return 0.00001;
    }

    private bool IsValidTradingSession()
    {
        int hour = DateTime.UtcNow.Hour;
        return (hour >= 8 && hour < 12) || (hour >= 13 && hour < 17);
    }

    // Called when bot starts - registers account
    public void RegisterAccount(string key, string accountId)
    {
        _accountIds[key] = accountId;
        Console.WriteLine($"Account registered: {key} -> {accountId}");
    }
}
