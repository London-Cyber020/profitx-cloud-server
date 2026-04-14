public class TradingEngine : BackgroundService
{
    private readonly DataStore _store;

    public TradingEngine(DataStore store)
    {
        _store = store;
        Console.WriteLine("Trading Engine v3.0 initialized - ICT/SMC Strategies");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Console.WriteLine("Trading Engine started - checking every 30 seconds");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                foreach (var bot in _store.ActiveBots.ToList())
                {
                    if (bot.Value.IsRunning)
                    {
                        await ProcessBot(bot.Key, bot.Value);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Engine error: {ex.Message}");
            }

            await Task.Delay(30000, stoppingToken);
        }
    }

    private async Task ProcessBot(string key, BotSession bot)
    {
        if (!_store.UserConnections.ContainsKey(key)) return;

        var conn = _store.UserConnections[key];
        if (string.IsNullOrEmpty(conn.MetaApiAccountId)) return;

        try
        {
            // Update account info
            var info = await MetaApiHelper.GetAccountInfo(conn.MetaApiToken, conn.MetaApiAccountId);
            if (info != null)
            {
                info.OpenTrades = (await MetaApiHelper.GetPositions(conn.MetaApiToken, conn.MetaApiAccountId)).Count;
                if (info.Balance > 0)
                    info.Drawdown = Math.Round(((info.Balance - info.Equity) / info.Balance) * 100, 2);
                _store.AccountsData[key] = info;
            }

            // Update open trades
            var positions = await MetaApiHelper.GetPositions(conn.MetaApiToken, conn.MetaApiAccountId);
            _store.OpenTrades[key] = positions;

            // Risk checks
            if (!PassesRiskChecks(bot, info)) return;

            // Session check
            if (!IsValidSession())
            {
                bot.Status = "Waiting for London/NY session";
                return;
            }

            // Max trades check
            if (positions.Count >= bot.MaxTrades)
            {
                bot.Status = $"Max trades reached ({positions.Count}/{bot.MaxTrades})";
                return;
            }

            // Get candles
            string timeframe = SelectTimeframe(bot, info);
            var candles = await MetaApiHelper.GetCandles(
                conn.MetaApiToken, conn.MetaApiAccountId, bot.Symbol, timeframe, 100);

            if (candles.Count < 20)
            {
                bot.Status = "Not enough candle data";
                return;
            }

            bot.Status = $"Analyzing {bot.Symbol} ({bot.Strategy}) on {timeframe}...";

            // Run strategy
            if (bot.Strategy == "ICT")
                await RunICTStrategy(conn, bot, candles);
            else if (bot.Strategy == "SMC")
                await RunSMCStrategy(conn, bot, candles);

            bot.Status = $"Running on {bot.Symbol} ({bot.Strategy})";
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Bot error: {ex.Message}");
            bot.Status = $"Error: {ex.Message}";
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // ICT STRATEGY - Order Blocks + FVG + Confluence
    // ═══════════════════════════════════════════════════════════════
    private async Task RunICTStrategy(UserMT5Connection conn, BotSession bot, List<CandleData> candles)
    {
        var (bid, ask) = await MetaApiHelper.GetPrice(conn.MetaApiToken, conn.MetaApiAccountId, bot.Symbol);
        if (bid == 0 || ask == 0) return;

        double point = GetPoint(bot.Symbol);
        if (point <= 0) return;

        string trend = GetTrend(candles);
        double atr = GetATR(candles, 14);
        double atrPips = atr / point;

        // Volatility filter - skip quiet markets
        if (atrPips < GetMinATR(bot.Symbol)) return;

        // ═══════════════════════════════════════════════════════════
        // BULLISH ICT SETUP
        // ═══════════════════════════════════════════════════════════
        if (trend == "BULLISH")
        {
            int obIndex = FindBullishOrderBlock(candles);

            if (obIndex > 0)
            {
                double obLow = candles[obIndex].Low;
                double obHigh = candles[obIndex].High;

                // Price must be in OB zone
                if (ask >= obLow && ask <= obHigh)
                {
                    // Count confluences
                    int confluences = 1; // OB is first

                    // Check FVG
                    if (HasBullishFVG(candles, point)) confluences++;

                    // Check momentum
                    if (HasBullishMomentum(candles)) confluences++;

                    // Check near support
                    if (IsNearSupport(candles, ask, point)) confluences++;

                    // Check higher timeframe trend
                    if (trend == "BULLISH") confluences++;

                    // Need 3+ confluences for high win rate
                    if (confluences >= 3)
                    {
                        double sl = Math.Round(obLow - 10 * point, GetDigits(bot.Symbol));
                        double slPips = (ask - sl) / point;
                        double tp = Math.Round(ask + slPips * 1.5 * point, GetDigits(bot.Symbol));

                        double lotSize = CalculateLotSize(bot, slPips, point);

                        Console.WriteLine($"═══════════════════════════════════════════");
                        Console.WriteLine($"ICT BUY SIGNAL ({confluences} confluences)");
                        Console.WriteLine($"  Symbol: {bot.Symbol}");
                        Console.WriteLine($"  Entry: {ask}");
                        Console.WriteLine($"  SL: {sl} ({slPips:F0} pips)");
                        Console.WriteLine($"  TP: {tp} ({slPips * 1.5:F0} pips)");
                        Console.WriteLine($"  Lot: {lotSize}");
                        Console.WriteLine($"  RR: 1:1.5");
                        Console.WriteLine($"═══════════════════════════════════════════");

                        bool success = await MetaApiHelper.PlaceTrade(
                            conn.MetaApiToken, conn.MetaApiAccountId,
                            "ORDER_TYPE_BUY", bot.Symbol, lotSize, sl, tp,
                            $"ICT-BUY-{confluences}conf");

                        if (success)
                        {
                            bot.TotalTrades++;
                            Console.WriteLine("ICT BUY placed successfully!");
                        }
                    }
                }
            }
        }

        // ═══════════════════════════════════════════════════════════
        // BEARISH ICT SETUP
        // ═══════════════════════════════════════════════════════════
        if (trend == "BEARISH")
        {
            int obIndex = FindBearishOrderBlock(candles);

            if (obIndex > 0)
            {
                double obLow = candles[obIndex].Low;
                double obHigh = candles[obIndex].High;

                if (bid >= obLow && bid <= obHigh)
                {
                    int confluences = 1;

                    if (HasBearishFVG(candles, point)) confluences++;
                    if (HasBearishMomentum(candles)) confluences++;
                    if (IsNearResistance(candles, bid, point)) confluences++;
                    if (trend == "BEARISH") confluences++;

                    if (confluences >= 3)
                    {
                        double sl = Math.Round(obHigh + 10 * point, GetDigits(bot.Symbol));
                        double slPips = (sl - bid) / point;
                        double tp = Math.Round(bid - slPips * 1.5 * point, GetDigits(bot.Symbol));

                        double lotSize = CalculateLotSize(bot, slPips, point);

                        Console.WriteLine($"═══════════════════════════════════════════");
                        Console.WriteLine($"ICT SELL SIGNAL ({confluences} confluences)");
                        Console.WriteLine($"  Symbol: {bot.Symbol} Entry: {bid}");
                        Console.WriteLine($"  SL: {sl} TP: {tp} Lot: {lotSize}");
                        Console.WriteLine($"═══════════════════════════════════════════");

                        bool success = await MetaApiHelper.PlaceTrade(
                            conn.MetaApiToken, conn.MetaApiAccountId,
                            "ORDER_TYPE_SELL", bot.Symbol, lotSize, sl, tp,
                            $"ICT-SELL-{confluences}conf");

                        if (success) bot.TotalTrades++;
                    }
                }
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // SMC STRATEGY - BOS + Market Structure + Pullback
    // ═══════════════════════════════════════════════════════════════
    private async Task RunSMCStrategy(UserMT5Connection conn, BotSession bot, List<CandleData> candles)
    {
        var (bid, ask) = await MetaApiHelper.GetPrice(conn.MetaApiToken, conn.MetaApiAccountId, bot.Symbol);
        if (bid == 0 || ask == 0) return;

        double point = GetPoint(bot.Symbol);
        if (point <= 0) return;

        string trend = GetTrend(candles);
        double atr = GetATR(candles, 14);
        double atrPips = atr / point;

        if (atrPips < GetMinATR(bot.Symbol)) return;

        // Find swing points
        double swingHigh = 0, swingLow = 999999;
        int highBar = 0, lowBar = 0;

        for (int i = 0; i < Math.Min(50, candles.Count); i++)
        {
            if (candles[i].High > swingHigh) { swingHigh = candles[i].High; highBar = i; }
            if (candles[i].Low < swingLow) { swingLow = candles[i].Low; lowBar = i; }
        }

        bool isBullishStructure = lowBar > highBar;

        // ═══════════════════════════════════════════════════════════
        // BULLISH BOS (Break of Structure)
        // ═══════════════════════════════════════════════════════════
        if (isBullishStructure && trend == "BULLISH")
        {
            double bosPips = (candles[0].High - swingHigh) / point;

            if (bosPips > 15)
            {
                // Wait for pullback
                bool hasPullback = false;
                for (int i = 1; i <= 5 && i < candles.Count; i++)
                {
                    if (candles[i].Low < swingHigh + 10 * point)
                    {
                        hasPullback = true;
                        break;
                    }
                }

                if (hasPullback && ask > swingHigh)
                {
                    // Check confluences
                    int confluences = 2; // BOS + Pullback

                    if (HasBullishMomentum(candles)) confluences++;
                    if (IsNearSupport(candles, ask, point)) confluences++;

                    if (confluences >= 3)
                    {
                        double sl = Math.Round(swingLow - 10 * point, GetDigits(bot.Symbol));
                        double slPips = (ask - sl) / point;
                        double tp = Math.Round(ask + slPips * 1.5 * point, GetDigits(bot.Symbol));

                        double lotSize = CalculateLotSize(bot, slPips, point);

                        Console.WriteLine($"SMC BUY SIGNAL ({confluences} conf) BOS:{bosPips:F0}pips");

                        bool success = await MetaApiHelper.PlaceTrade(
                            conn.MetaApiToken, conn.MetaApiAccountId,
                            "ORDER_TYPE_BUY", bot.Symbol, lotSize, sl, tp,
                            $"SMC-BUY-BOS{bosPips:F0}");

                        if (success) bot.TotalTrades++;
                    }
                }
            }
        }

        // ═══════════════════════════════════════════════════════════
        // BEARISH BOS
        // ═══════════════════════════════════════════════════════════
        if (!isBullishStructure && trend == "BEARISH")
        {
            double bosPips = (swingLow - candles[0].Low) / point;

            if (bosPips > 15)
            {
                bool hasPullback = false;
                for (int i = 1; i <= 5 && i < candles.Count; i++)
                {
                    if (candles[i].High > swingLow - 10 * point)
                    {
                        hasPullback = true;
                        break;
                    }
                }

                if (hasPullback && bid < swingLow)
                {
                    int confluences = 2;
                    if (HasBearishMomentum(candles)) confluences++;
                    if (IsNearResistance(candles, bid, point)) confluences++;

                    if (confluences >= 3)
                    {
                        double sl = Math.Round(swingHigh + 10 * point, GetDigits(bot.Symbol));
                        double slPips = (sl - bid) / point;
                        double tp = Math.Round(bid - slPips * 1.5 * point, GetDigits(bot.Symbol));

                        double lotSize = CalculateLotSize(bot, slPips, point);

                        Console.WriteLine($"SMC SELL SIGNAL ({confluences} conf) BOS:{bosPips:F0}pips");

                        bool success = await MetaApiHelper.PlaceTrade(
                            conn.MetaApiToken, conn.MetaApiAccountId,
                            "ORDER_TYPE_SELL", bot.Symbol, lotSize, sl, tp,
                            $"SMC-SELL-BOS{bosPips:F0}");

                        if (success) bot.TotalTrades++;
                    }
                }
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // HELPER FUNCTIONS
    // ═══════════════════════════════════════════════════════════════

    private bool PassesRiskChecks(BotSession bot, AccountData? info)
    {
        if (info == null) return false;

        // Daily loss limit (5%)
        if (bot.DailyStartBalance > 0)
        {
            double dailyPL = ((info.Balance - bot.DailyStartBalance) / bot.DailyStartBalance) * 100;
            if (dailyPL <= -5)
            {
                bot.Status = "Daily loss limit reached (-5%)";
                bot.IsRunning = false;
                return false;
            }
        }

        // Max drawdown (10%)
        if (info.Balance > 0)
        {
            double drawdown = ((info.Balance - info.Equity) / info.Balance) * 100;
            if (drawdown >= 10)
            {
                bot.Status = "Max drawdown reached (10%)";
                bot.IsRunning = false;
                return false;
            }
        }

        return true;
    }

    private string SelectTimeframe(BotSession bot, AccountData? info)
    {
        double balance = info?.Balance ?? 0;

        // Micro accounts = slower timeframes
        if (balance < 50) return "30m";
        if (balance < 100) return "15m";

        string symbol = bot.Symbol.ToUpper();
        if (symbol.Contains("BTC") || symbol.Contains("ETH")) return "30m";
        if (symbol.Contains("XAU") || symbol.Contains("GOLD")) return "15m";
        if (symbol.Contains("US30") || symbol.Contains("US100")) return "5m";

        int hour = DateTime.UtcNow.Hour;
        if (hour >= 13 && hour < 17) return "15m"; // Overlap
        if (hour >= 8 && hour < 13) return "15m"; // London
        return "30m"; // Off hours
    }

    private bool IsValidSession()
    {
        int hour = DateTime.UtcNow.Hour;
        return (hour >= 8 && hour < 12) || (hour >= 13 && hour < 17);
    }

    private string GetTrend(List<CandleData> candles)
    {
        if (candles.Count < 20) return "UNKNOWN";

        double sum5 = 0, sum20 = 0;
        for (int i = 0; i < 5; i++) sum5 += candles[i].Close;
        for (int i = 0; i < 20; i++) sum20 += candles[i].Close;

        double ma5 = sum5 / 5;
        double ma20 = sum20 / 20;

        if (candles[0].Close > ma5 && ma5 > ma20) return "BULLISH";
        if (candles[0].Close < ma5 && ma5 < ma20) return "BEARISH";
        return "RANGING";
    }

    private double GetATR(List<CandleData> candles, int period)
    {
        if (candles.Count < period + 1) return 0;

        double sum = 0;
        for (int i = 0; i < period; i++)
        {
            double tr = Math.Max(candles[i].High - candles[i].Low,
                        Math.Max(Math.Abs(candles[i].High - candles[i + 1].Close),
                                 Math.Abs(candles[i].Low - candles[i + 1].Close)));
            sum += tr;
        }
        return sum / period;
    }

    private int FindBullishOrderBlock(List<CandleData> candles)
    {
        int consecutive = 0;
        for (int i = 2; i < Math.Min(50, candles.Count); i++)
        {
            if (candles[i].Close < candles[i].Open)
            {
                consecutive++;
                if (consecutive >= 3 && candles[1].Close > candles[i].High)
                    return i;
            }
            else consecutive = 0;
        }
        return 0;
    }

    private int FindBearishOrderBlock(List<CandleData> candles)
    {
        int consecutive = 0;
        for (int i = 2; i < Math.Min(50, candles.Count); i++)
        {
            if (candles[i].Close > candles[i].Open)
            {
                consecutive++;
                if (consecutive >= 3 && candles[1].Close < candles[i].Low)
                    return i;
            }
            else consecutive = 0;
        }
        return 0;
    }

    private bool HasBullishFVG(List<CandleData> candles, double point)
    {
        if (candles.Count < 4) return false;
        return (candles[1].Low - candles[3].High) / point > 5;
    }

    private bool HasBearishFVG(List<CandleData> candles, double point)
    {
        if (candles.Count < 4) return false;
        return (candles[3].Low - candles[1].High) / point > 5;
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

    private bool IsNearSupport(List<CandleData> candles, double price, double point)
    {
        double lowest = candles.Take(20).Min(c => c.Low);
        return Math.Abs(price - lowest) / point < 20;
    }

    private bool IsNearResistance(List<CandleData> candles, double price, double point)
    {
        double highest = candles.Take(20).Max(c => c.High);
        return Math.Abs(price - highest) / point < 20;
    }

    private double CalculateLotSize(BotSession bot, double slPips, double point)
    {
        if (bot.LotSize > 0) return bot.LotSize;
        return 0.01;
    }

    private double GetPoint(string symbol)
    {
        string s = symbol.ToUpper();
        if (s.Contains("JPY")) return 0.001;
        if (s.Contains("XAU") || s.Contains("GOLD")) return 0.01;
        if (s.Contains("BTC")) return 1.0;
        if (s.Contains("US30") || s.Contains("US100")) return 0.1;
        return 0.00001;
    }

    private int GetDigits(string symbol)
    {
        string s = symbol.ToUpper();
        if (s.Contains("JPY")) return 3;
        if (s.Contains("XAU") || s.Contains("GOLD")) return 2;
        if (s.Contains("BTC")) return 0;
        if (s.Contains("US30") || s.Contains("US100")) return 1;
        return 5;
    }

    private double GetMinATR(string symbol)
    {
        string s = symbol.ToUpper();
        if (s.Contains("XAU") || s.Contains("GOLD")) return 50;
        if (s.Contains("BTC")) return 100;
        if (s.Contains("US30") || s.Contains("US100")) return 30;
        return 10;
    }
}
