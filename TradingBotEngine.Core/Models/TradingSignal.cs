using System;

namespace TradingBotEngine.Core.Models
{
    public class TradingSignal
    {
        public string Symbol { get; set; }
        public string Action { get; set; }  // BUY, SELL, HOLD
        public decimal Confidence { get; set; }
        public string Reason { get; set; }
        public string Pattern { get; set; }
        public decimal Rsi { get; set; }
        public string Trend { get; set; }
        public decimal Support { get; set; }
        public decimal Resistance { get; set; }
        public decimal? StopLoss { get; set; }
        public decimal? TakeProfit { get; set; }
        public decimal Price { get; set; }
        public string Indicators { get; set; }
        public DateTime GeneratedAt { get; set; }
    }
}