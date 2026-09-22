using System.Globalization;
using System.Text.Json;
using TradingBotEngine.Core.Models;

namespace TradingBotEngine.Core
{
    public class SignalGenerator
    {
        private static readonly HashSet<string> BullishPatterns = new(StringComparer.OrdinalIgnoreCase)
        {
            "HeadAndShouldersInverse"
        };

        private static readonly HashSet<string> BearishPatterns = new(StringComparer.OrdinalIgnoreCase)
        {
            "HeadAndShoulders"
        };

        public Task<TradingSignal> GenerateSignal(
            string symbol,
            List<Candle> candles,
            Dictionary<string, object> indicators)
        {
            if (string.IsNullOrWhiteSpace(symbol))
                throw new ArgumentException("Symbol is required.", nameof(symbol));

            if (candles == null || candles.Count == 0)
                throw new ArgumentException("At least one candle is required to generate a signal.", nameof(candles));

            if (indicators == null)
                throw new ArgumentNullException(nameof(indicators));

            var currentPrice = (decimal)candles[^1].Close;
            var signal = new TradingSignal
            {
                Symbol = symbol.Trim().ToUpperInvariant(),
                GeneratedAt = DateTime.UtcNow,
                Price = currentPrice,
                Action = "HOLD",
                Confidence = 50,
                Reason = "Signal generation requires valid indicator data.",
                Pattern = "None",
                Trend = "Sideways"
            };

            // These are the core values used by the weighted decision model.
            // Pattern is supporting evidence, not a hard requirement. Requiring
            // one rare pattern before every trade was the main reason the old
            // generator returned HOLD so frequently.
            if (!TryGetDecimal(indicators, "RSI", out var rsi) ||
                !TryGetDecimal(indicators, "MA50", out var ma50) ||
                !TryGetDecimal(indicators, "MA200", out var ma200) ||
                !TryGetDecimal(indicators, "EMA50", out var ema50) ||
                !TryGetDecimal(indicators, "EMA200", out var ema200) ||
                !TryGetDecimal(indicators, "MACD", out var macd) ||
                !TryGetDecimal(indicators, "MACDSignal", out var macdSignal) ||
                !TryGetDecimal(indicators, "BBMiddle", out var bbMiddle) ||
                !TryGetDecimal(indicators, "Volume", out var volume) ||
                !TryGetDecimal(indicators, "AverageVolume", out var averageVolume) ||
                !TryGetDecimal(indicators, "TrendStrength", out var trendStrength) ||
                !TryGetDecimal(indicators, "Support", out var support) ||
                !TryGetDecimal(indicators, "Resistance", out var resistance) ||
                !TryGetString(indicators, "Pattern", out var pattern) ||
                !TryGetString(indicators, "Trend", out var trend))
            {
                signal.Reason = "Signal held because one or more required indicators are missing or invalid.";
                signal.Indicators = SerializeIndicators(indicators);
                return Task.FromResult(signal);
            }

            if (currentPrice <= 0m ||
                ma50 <= 0m ||
                ma200 <= 0m ||
                ema50 <= 0m ||
                ema200 <= 0m ||
                bbMiddle <= 0m ||
                support <= 0m ||
                resistance <= 0m ||
                resistance <= support ||
                volume < 0m ||
                averageVolume <= 0m ||
                trendStrength < 0m)
            {
                signal.Reason = "Signal held because market or indicator values are invalid.";
                signal.Indicators = SerializeIndicators(indicators);
                return Task.FromResult(signal);
            }

            signal.Rsi = rsi;
            signal.Trend = trend;
            signal.Pattern = pattern;
            signal.Support = support;
            signal.Resistance = resistance;
            signal.Indicators = SerializeIndicators(indicators);

            // Weighted confirmation model.
            // Each direction can earn up to 10 points. A pattern is deliberately
            // optional because the current pattern detector only identifies two
            // specific formations. This lets the other indicators make a decision.
            var bullishScore = 0;
            var bearishScore = 0;
            var bullishReasons = new List<string>();
            var bearishReasons = new List<string>();

            if (currentPrice > ma50)
            {
                bullishScore++;
                bullishReasons.Add("Price above MA50");
            }
            else if (currentPrice < ma50)
            {
                bearishScore++;
                bearishReasons.Add("Price below MA50");
            }

            if (ma50 > ma200)
            {
                bullishScore++;
                bullishReasons.Add("MA50 above MA200");
            }
            else if (ma50 < ma200)
            {
                bearishScore++;
                bearishReasons.Add("MA50 below MA200");
            }

            if (ema50 > ema200)
            {
                bullishScore++;
                bullishReasons.Add("EMA50 above EMA200");
            }
            else if (ema50 < ema200)
            {
                bearishScore++;
                bearishReasons.Add("EMA50 below EMA200");
            }

            if (macd > macdSignal)
            {
                bullishScore++;
                bullishReasons.Add("MACD above signal");
            }
            else if (macd < macdSignal)
            {
                bearishScore++;
                bearishReasons.Add("MACD below signal");
            }

            if (currentPrice > bbMiddle)
            {
                bullishScore++;
                bullishReasons.Add("Price above Bollinger middle band");
            }
            else if (currentPrice < bbMiddle)
            {
                bearishScore++;
                bearishReasons.Add("Price below Bollinger middle band");
            }

            if (rsi >= 52m && rsi <= 68m)
            {
                bullishScore++;
                bullishReasons.Add($"RSI {rsi:F1} supports bullish momentum");
            }
            else if (rsi <= 48m && rsi >= 32m)
            {
                bearishScore++;
                bearishReasons.Add($"RSI {rsi:F1} supports bearish momentum");
            }

            if (trend.Equals("Bullish", StringComparison.OrdinalIgnoreCase))
            {
                bullishScore++;
                bullishReasons.Add("Bullish trend");
            }
            else if (trend.Equals("Bearish", StringComparison.OrdinalIgnoreCase))
            {
                bearishScore++;
                bearishReasons.Add("Bearish trend");
            }

            if (trendStrength >= 20m)
            {
                if (trend.Equals("Bullish", StringComparison.OrdinalIgnoreCase))
                {
                    bullishScore++;
                    bullishReasons.Add($"Trend strength {trendStrength:F1}");
                }
                else if (trend.Equals("Bearish", StringComparison.OrdinalIgnoreCase))
                {
                    bearishScore++;
                    bearishReasons.Add($"Trend strength {trendStrength:F1}");
                }
            }

            if (averageVolume > 0m && volume >= averageVolume)
            {
                if (bullishScore > bearishScore)
                {
                    bullishScore++;
                    bullishReasons.Add("Volume at or above average");
                }
                else if (bearishScore > bullishScore)
                {
                    bearishScore++;
                    bearishReasons.Add("Volume at or above average");
                }
            }

            // The existing pattern detector is useful confirmation when it fires,
            // but it no longer blocks otherwise valid multi-indicator setups.
            if (BullishPatterns.Contains(pattern))
            {
                bullishScore++;
                bullishReasons.Add($"Bullish {pattern} pattern");
            }
            else if (BearishPatterns.Contains(pattern))
            {
                bearishScore++;
                bearishReasons.Add($"Bearish {pattern} pattern");
            }

            const int minimumScore = 5;
            const int minimumScoreLead = 2;

            var bullishSetup = bullishScore >= minimumScore &&
                               bullishScore >= bearishScore + minimumScoreLead &&
                               currentPrice > support &&
                               currentPrice < resistance;

            var bearishSetup = bearishScore >= minimumScore &&
                               bearishScore >= bullishScore + minimumScoreLead &&
                               currentPrice > support &&
                               currentPrice < resistance;

            if (bullishSetup)
            {
                signal.Action = "BUY";
                signal.StopLoss = support;
                signal.TakeProfit = resistance;
                signal.Confidence = CalculateScoreConfidence(bullishScore, bearishScore, trendStrength, rsi);
                signal.Reason = string.Join("; ", bullishReasons);
            }
            else if (bearishSetup)
            {
                signal.Action = "SELL";
                signal.StopLoss = resistance;
                signal.TakeProfit = support;
                signal.Confidence = CalculateScoreConfidence(bearishScore, bullishScore, trendStrength, rsi);
                signal.Reason = string.Join("; ", bearishReasons);
            }
            else
            {
                signal.Action = "HOLD";
                signal.Confidence = CalculateHoldConfidence(bullishScore, bearishScore);
                signal.Reason = $"HOLD: bullish score {bullishScore}/10, bearish score {bearishScore}/10. " +
                                "No direction has enough confirmation and separation for an entry.";
            }

            return Task.FromResult(signal);
        }

        private static decimal CalculateScoreConfidence(
            int directionalScore,
            int opposingScore,
            decimal trendStrength,
            decimal rsi)
        {
            // Base confidence comes from the amount of independent confirmation,
            // not from a hard-coded 50% value.
            var confidence = 45m + (directionalScore * 5m) +
                             Math.Min(15m, Math.Max(0m, directionalScore - opposingScore) * 3m);

            if (trendStrength >= 25m)
                confidence += 5m;

            if (rsi >= 55m && rsi <= 65m)
                confidence += 3m;
            else if (rsi <= 45m && rsi >= 35m)
                confidence += 3m;

            return Math.Clamp(confidence, 50m, 95m);
        }

        private static decimal CalculateHoldConfidence(int bullishScore, int bearishScore)
        {
            // HOLD confidence describes how balanced the evidence is. It is not
            // presented as a probability of the market falling or rising.
            var balance = Math.Abs(bullishScore - bearishScore);
            return Math.Clamp(50m + (balance * 5m), 50m, 80m);
        }

        private static bool TryGetDecimal(
            Dictionary<string, object> indicators,
            string key,
            out decimal value)
        {
            value = 0m;

            if (!indicators.TryGetValue(key, out var raw) || raw == null)
                return false;

            try
            {
                value = raw switch
                {
                    decimal decimalValue => decimalValue,
                    double doubleValue when !double.IsNaN(doubleValue) && !double.IsInfinity(doubleValue) => (decimal)doubleValue,
                    float floatValue when !float.IsNaN(floatValue) && !float.IsInfinity(floatValue) => (decimal)floatValue,
                    int intValue => intValue,
                    long longValue => longValue,
                    _ => Convert.ToDecimal(raw, CultureInfo.InvariantCulture)
                };

                return value != decimal.MinValue && value != decimal.MaxValue;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryGetString(
            Dictionary<string, object> indicators,
            string key,
            out string value)
        {
            value = string.Empty;

            if (!indicators.TryGetValue(key, out var raw) || raw == null)
                return false;

            value = raw.ToString()?.Trim() ?? string.Empty;
            return !string.IsNullOrWhiteSpace(value);
        }

        private static string SerializeIndicators(Dictionary<string, object> indicators)
        {
            try
            {
                return JsonSerializer.Serialize(indicators);
            }
            catch
            {
                return "{}";
            }
        }
    }
}
