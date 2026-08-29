using TradingBotEngine.Core.Models;

namespace TradingBotEngine.Core
{
    public class SignalGenerator
    {
        public async Task<TradingSignal> GenerateSignal(
            string symbol,
            List<Candle> candles,
            Dictionary<string, object> indicators)
        {
            var signal = new TradingSignal
            {
                Symbol = symbol,
                GeneratedAt = DateTime.UtcNow
            };

            // Extract indicators
            var rsi = (decimal)indicators["RSI"];
            var ma50 = (decimal)indicators["MA50"];
            var ma200 = (decimal)indicators["MA200"];
            var currentPrice = (decimal)candles.Last().Close;
            var pattern = (string)indicators["Pattern"];
            var trend = (string)indicators["Trend"];
            var support = (decimal)indicators["Support"];
            var resistance = (decimal)indicators["Resistance"];

            // BUY CONDITIONS
            bool buyCondition = 
                rsi > 50 &&                                    // RSI above 50
                currentPrice > ma50 &&                         // Price above 50MA
                trend == "Bullish" &&                          // Uptrend (HH + HL)
                (pattern == "BullFlag" || pattern == "DoubleBottom" || 
                 pattern == "HeadAndShouldersInverse") &&     // Bullish pattern
                currentPrice > support &&                     // Above support
                currentPrice < resistance;                    // Below resistance

            // SELL CONDITIONS
            bool sellCondition = 
                rsi < 50 &&                                    // RSI below 50
                currentPrice < ma50 &&                         // Price below 50MA
                trend == "Bearish" &&                          // Downtrend (LH + LL)
                (pattern == "BearFlag" || pattern == "DoubleTop" || 
                 pattern == "HeadAndShoulders") &&            // Bearish pattern
                currentPrice < resistance &&                  // Below resistance
                currentPrice > support;                       // Above support

            if (buyCondition)
            {
                signal.Action = "BUY";
                signal.Price = currentPrice;
                signal.StopLoss = support;
                signal.TakeProfit = resistance;
                signal.Confidence = CalculateConfidence(indicators, "BUY");
                signal.Reason = GenerateReason(indicators, "BUY");
                signal.Pattern = pattern;
                signal.Rsi = rsi;
                signal.Trend = trend;
                signal.Support = support;
                signal.Resistance = resistance;
                signal.Indicators = System.Text.Json.JsonSerializer.Serialize(indicators);
            }
            else if (sellCondition)
            {
                signal.Action = "SELL";
                signal.Price = currentPrice;
                signal.StopLoss = resistance;
                signal.TakeProfit = support;
                signal.Confidence = CalculateConfidence(indicators, "SELL");
                signal.Reason = GenerateReason(indicators, "SELL");
                signal.Pattern = pattern;
                signal.Rsi = rsi;
                signal.Trend = trend;
                signal.Support = support;
                signal.Resistance = resistance;
                signal.Indicators = System.Text.Json.JsonSerializer.Serialize(indicators);
            }
            else
            {
                signal.Action = "HOLD";
                signal.Price = currentPrice;
                signal.Confidence = 50;
                signal.Reason = "Sideways/Ranging market detected";
                signal.Trend = "Sideways";
            }

            return signal;
        }

        private decimal CalculateConfidence(Dictionary<string, object> indicators, string action)
        {
            // Calculate confidence based on indicator strength
            var rsi = (decimal)indicators["RSI"];
            var volume = (decimal)indicators["Volume"];
            var avgVolume = (decimal)indicators["AverageVolume"];
            var trendStrength = (decimal)indicators["TrendStrength"];

            decimal confidence = 50;

            if (action == "BUY")
            {
                if (rsi > 60) confidence += 10;
                if (rsi < 40) confidence -= 10;
                if (volume > avgVolume * 1.5m) confidence += 10;
                if (trendStrength > 70) confidence += 10;
            }
            else if (action == "SELL")
            {
                if (rsi < 40) confidence += 10;
                if (rsi > 60) confidence -= 10;
                if (volume > avgVolume * 1.5m) confidence += 10;
                if (trendStrength > 70) confidence += 10;
            }

            return Math.Clamp(confidence, 0, 100);
        }

        private string GenerateReason(Dictionary<string, object> indicators, string action)
        {
            var reasons = new List<string>();

            if (action == "BUY")
            {
                if ((decimal)indicators["RSI"] > 50) 
                    reasons.Add("RSI above 50 (bullish momentum)");
                if ((decimal)indicators["Price"] > (decimal)indicators["MA50"]) 
                    reasons.Add("Price above 50MA (bullish trend)");
                if (!string.IsNullOrEmpty((string)indicators["Pattern"])) 
                    reasons.Add($"Bullish {indicators["Pattern"]} pattern detected");
                if ((string)indicators["Trend"] == "Bullish") 
                    reasons.Add("Uptrend confirmed (Higher High + Higher Low)");
                if ((decimal)indicators["Volume"] > (decimal)indicators["AverageVolume"] * 1.5m) 
                    reasons.Add("Increasing buying volume");
            }
            else if (action == "SELL")
            {
                if ((decimal)indicators["RSI"] < 50) 
                    reasons.Add("RSI below 50 (bearish momentum)");
                if ((decimal)indicators["Price"] < (decimal)indicators["MA50"]) 
                    reasons.Add("Price below 50MA (bearish trend)");
                if (!string.IsNullOrEmpty((string)indicators["Pattern"])) 
                    reasons.Add($"Bearish {indicators["Pattern"]} pattern detected");
                if ((string)indicators["Trend"] == "Bearish") 
                    reasons.Add("Downtrend confirmed (Lower High + Lower Low)");
                if ((decimal)indicators["Volume"] > (decimal)indicators["AverageVolume"] * 1.5m) 
                    reasons.Add("Increasing selling volume");
            }

            return string.Join("; ", reasons);
        }
    }

}
