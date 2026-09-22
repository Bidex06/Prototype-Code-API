using Skender.Stock.Indicators;
using TradingBotEngine.Core.Models;

namespace TradingBotEngine.Core
{
    public class IndicatorCalculator
    {
        public Task<Dictionary<string, object>> CalculateIndicators(
            List<Candle> candles,
            Dictionary<string, object>? parameters = null)
        {
            if (candles == null || candles.Count < 200)
            {
                throw new ArgumentException("At least 200 candles are required to calculate the configured indicators.", nameof(candles));
            }

            var indicators = new Dictionary<string, object>();
            var quotes = candles.Select(c => new Quote
            {
                Date = c.Timestamp,
                Open = (decimal)c.Open,
                High = (decimal)c.High,
                Low = (decimal)c.Low,
                Close = (decimal)c.Close,
                Volume = (decimal)c.Volume
            }).ToList();

            // RSI (14 periods)
            var rsi = quotes.GetRsi(14);
            indicators["RSI"] = rsi.Last().Rsi ?? 50;

            // Moving Averages (50 and 200)
            var sma50 = quotes.GetSma(50);
            var sma200 = quotes.GetSma(200);
            indicators["MA50"] = sma50.Last().Sma ?? 0;
            indicators["MA200"] = sma200.Last().Sma ?? 0;

            // EMA (50 and 200)
            var ema50 = quotes.GetEma(50);
            var ema200 = quotes.GetEma(200);
            indicators["EMA50"] = ema50.Last().Ema ?? 0;
            indicators["EMA200"] = ema200.Last().Ema ?? 0;

            // MACD
            var macd = quotes.GetMacd(12, 26, 9);
            indicators["MACD"] = macd.Last().Macd ?? 0;
            indicators["MACDSignal"] = macd.Last().Signal ?? 0;
            indicators["MACDHistogram"] = macd.Last().Histogram ?? 0;

            // Bollinger Bands
            var bb = quotes.GetBollingerBands(20, 2);
            indicators["BBUpper"] = bb.Last().UpperBand ?? 0;
            indicators["BBLower"] = bb.Last().LowerBand ?? 0;
            indicators["BBMiddle"] = bb.Last().Sma ?? 0;

            // Price & Volume
            var lastCandle = candles.Last();
            indicators["Price"] = (decimal)lastCandle.Close;
            indicators["Volume"] = (decimal)lastCandle.Volume;
    indicators["AverageVolume"] = (decimal)candles.TakeLast(20).Average(c => c.Volume);

            // Trend Strength (using ADX)
            var adx = quotes.GetAdx(14);
            indicators["TrendStrength"] = adx.Last().Adx ?? 0;

            // Support & Resistance
            var (support, resistance) = FindSupportAndResistance(candles);
            indicators["Support"] = support;
            indicators["Resistance"] = resistance;

            // Trend
            var ma50 = Convert.ToDecimal(indicators["MA50"], System.Globalization.CultureInfo.InvariantCulture);
            var ma200 = Convert.ToDecimal(indicators["MA200"], System.Globalization.CultureInfo.InvariantCulture);
            indicators["Trend"] = DetermineTrend(candles, ma50, ma200);

            // Pattern
            indicators["Pattern"] = DetectPattern(candles, quotes);

            return Task.FromResult(indicators);
        }

        private (decimal Support, decimal Resistance) FindSupportAndResistance(List<Candle> candles)
        {
            // Simple implementation: find local minima and maxima
            var highs = candles.Select(c => c.High).ToList();
            var lows = candles.Select(c => c.Low).ToList();
            
            var resistance = highs.OrderByDescending(h => h).Take(2).Average();
            var support = lows.OrderBy(l => l).Take(2).Average();

            return ((decimal)support, (decimal)resistance);
        }

        private string DetermineTrend(List<Candle> candles, decimal ma50, decimal ma200)
        {
            var price = (decimal)candles.Last().Close;
            
            if (price > ma50 && ma50 > ma200)
                return "Bullish";
            else if (price < ma50 && ma50 < ma200)
                return "Bearish";
            else
                return "Sideways";
        }

        private string DetectPattern(List<Candle> candles, List<Quote> quotes)
        {
            // Simplified pattern detection
            // In production, use TA-Lib for full pattern recognition
            
            var pattern = "None";
            
            // Check for Head & Shoulders (simplified)
            if (candles.Count > 100)
            {
                var leftShoulder = candles[^30].High;
                var head = candles[^15].High;
                var rightShoulder = candles[^5].High;
                
                if (head > leftShoulder && head > rightShoulder && 
                    Math.Abs(leftShoulder - rightShoulder) / leftShoulder < 0.05)
                {
                    pattern = "HeadAndShoulders";
                }
                
                // Inverse Head & Shoulders
                var leftShoulderLow = candles[^30].Low;
                var headLow = candles[^15].Low;
                var rightShoulderLow = candles[^5].Low;
                
                if (headLow < leftShoulderLow && headLow < rightShoulderLow && 
                    Math.Abs(leftShoulderLow - rightShoulderLow) / leftShoulderLow < 0.05)
                {
                    pattern = "HeadAndShouldersInverse";
                }
            }

            return pattern;
        }
    }

}