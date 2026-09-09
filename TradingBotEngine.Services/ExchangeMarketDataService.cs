using Binance.Net.Clients;
using Bybit.Net.Clients;
using Bybit.Net.Enums;

namespace TradingBotEngine.Services;

public sealed class ExchangeMarketDataService
{
    public async Task<ExchangeOrderRules> GetBinanceRulesAsync(
        string symbol,
        bool useFutures)
    {
        if (string.IsNullOrWhiteSpace(symbol))
            throw new ArgumentException("Symbol is required.", nameof(symbol));

        symbol = symbol.Trim().ToUpperInvariant();

        var client = new BinanceRestClient();

        if (useFutures)
        {
            var info = await client.UsdFuturesApi.ExchangeData
                .GetExchangeInfoAsync();

            if (!info.Success)
                throw new InvalidOperationException(
                    $"Unable to get Binance Futures exchange info: {info.Error}");

            var market = info.Data.Symbols
                .FirstOrDefault(x =>
                    string.Equals(x.Name, symbol, StringComparison.OrdinalIgnoreCase));

            if (market == null)
                throw new InvalidOperationException(
                    $"Binance Futures symbol '{symbol}' was not found.");

            var ticker = await client.UsdFuturesApi.ExchangeData
                .GetPriceAsync(symbol);

            if (!ticker.Success)
                throw new InvalidOperationException(
                    $"Unable to get Binance Futures price for {symbol}: {ticker.Error}");

            var lot = market.LotSizeFilter
                ?? throw new InvalidOperationException(
                    $"Binance Futures returned no lot-size rules for {symbol}.");

            return new ExchangeOrderRules
            {
                MinQuantity = lot.MinQuantity,
                MaxQuantity = lot.MaxQuantity,
                QuantityStep = lot.StepSize,
                MinNotional = 0m,
                MaxLeverage = 1m,
                ReferencePrice = ticker.Data.Price
            };
        }

        var spotInfo = await client.SpotApi.ExchangeData
            .GetExchangeInfoAsync(symbol);

        if (!spotInfo.Success)
            throw new InvalidOperationException(
                $"Unable to get Binance Spot exchange info: {spotInfo.Error}");

        var spotMarket = spotInfo.Data.Symbols
            .FirstOrDefault(x =>
                string.Equals(x.Name, symbol, StringComparison.OrdinalIgnoreCase));

        if (spotMarket == null)
            throw new InvalidOperationException(
                $"Binance Spot symbol '{symbol}' was not found.");

        var spotTicker = await client.SpotApi.ExchangeData
            .GetTickerAsync(symbol);

        if (!spotTicker.Success)
            throw new InvalidOperationException(
                $"Unable to get Binance Spot price for {symbol}: {spotTicker.Error}");

        var spotLot = spotMarket.LotSizeFilter
            ?? throw new InvalidOperationException(
                $"Binance Spot returned no lot-size rules for {symbol}.");

        return new ExchangeOrderRules
        {
            MinQuantity = spotLot.MinQuantity,
            MaxQuantity = spotLot.MaxQuantity,
            QuantityStep = spotLot.StepSize,
            MinNotional = 0m,
            MaxLeverage = 1m,
            ReferencePrice = spotTicker.Data.LastPrice
        };
    }

    public async Task<ExchangeOrderRules> GetBybitRulesAsync(
        string symbol,
        bool useFutures)
    {
        if (string.IsNullOrWhiteSpace(symbol))
            throw new ArgumentException("Symbol is required.", nameof(symbol));

        symbol = symbol.Trim().ToUpperInvariant();

        var client = new BybitRestClient();

        if (useFutures)
        {
            var info = await client.V5Api.ExchangeData
                .GetLinearInverseSymbolsAsync(
                    Category.Linear,
                    symbol);

            if (!info.Success)
                throw new InvalidOperationException(
                    $"Unable to get Bybit Futures rules for {symbol}: {info.Error}");

            var market = info.Data.List.FirstOrDefault();

            if (market == null)
                throw new InvalidOperationException(
                    $"Bybit Futures symbol '{symbol}' was not found.");

            var ticker = await client.V5Api.ExchangeData
                .GetLinearInverseTickersAsync(
                    Category.Linear,
                    symbol);

            if (!ticker.Success)
                throw new InvalidOperationException(
                    $"Unable to get Bybit Futures price for {symbol}: {ticker.Error}");

            var tickerData = ticker.Data.List.FirstOrDefault();

            if (tickerData == null)
                throw new InvalidOperationException(
                    $"Bybit returned no ticker for {symbol}.");

            var lot = market.LotSizeFilter
                ?? throw new InvalidOperationException(
                    $"Bybit Futures returned no lot-size rules for {symbol}.");

            return new ExchangeOrderRules
            {
                MinQuantity = lot.MinOrderQuantity,
                MaxQuantity = lot.MaxOrderQuantity,
                QuantityStep = lot.QuantityStep,
                MinNotional = 0m,
                MaxLeverage = market.LeverageFilter?.MaxLeverage ?? 1m,
                ReferencePrice = tickerData.LastPrice
            };
        }

        var spotInfo = await client.V5Api.ExchangeData
            .GetSpotSymbolsAsync(symbol);

        if (!spotInfo.Success)
            throw new InvalidOperationException(
                $"Unable to get Bybit Spot rules for {symbol}: {spotInfo.Error}");

        var spotMarket = spotInfo.Data.List.FirstOrDefault();

        if (spotMarket == null)
            throw new InvalidOperationException(
                $"Bybit Spot symbol '{symbol}' was not found.");

        var spotTicker = await client.V5Api.ExchangeData
            .GetSpotTickersAsync(symbol);

        if (!spotTicker.Success)
            throw new InvalidOperationException(
                $"Unable to get Bybit Spot price for {symbol}: {spotTicker.Error}");

        var spotTickerData = spotTicker.Data.List.FirstOrDefault();

        if (spotTickerData == null)
            throw new InvalidOperationException(
                $"Bybit returned no Spot ticker for {symbol}.");

        var spotLot = spotMarket.LotSizeFilter
            ?? throw new InvalidOperationException(
                $"Bybit Spot returned no lot-size rules for {symbol}.");

        return new ExchangeOrderRules
        {
            MinQuantity = spotLot.MinOrderQuantity,
            MaxQuantity = spotLot.MaxOrderQuantity,
            QuantityStep = spotLot.BasePrecision,
            MinNotional = spotLot.MinOrderValue,
            MaxLeverage = 1m,
            ReferencePrice = spotTickerData.LastPrice
        };
    }
}