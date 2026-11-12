using RiskPilot.Diagnostics.Nt8.Core.Models;
using RiskPilot.Diagnostics.Nt8.Core.Session;

namespace RiskPilot.Diagnostics.Nt8.Core.Engine.Consumers
{
    /// <summary>
    /// M6 event consumer. Runs on the drain thread and accumulates raw trade-direction
    /// provenance: buy/sell/unknown counts, the unknown-direction cause, whether any
    /// direction was provider-supplied, and whether a stream reset was observed. A
    /// quote-derived direction is counted as derived, never as provider-confirmed.
    /// </summary>
    public sealed class M6TradeDeltaConsumer : INormalizedEventConsumer
    {
        private readonly SessionContext _session;

        public M6TradeDeltaConsumer(SessionContext session)
        {
            _session = session;
        }

        public string Name { get { return "M6.TradeDelta"; } }

        public void Consume(NormalizedRecord record)
        {
            if (_session == null || !(record is MarketDataRecord marketData))
            {
                return;
            }

            var counters = _session.Counters;

            if (marketData.IsReset)
            {
                counters.Increment(RuntimeCounterKeys.TradeDeltaResetObserved);
            }

            if (marketData.Kind != MarketDataKind.Trade)
            {
                return;
            }

            switch (marketData.TradeDirection)
            {
                case TradeDirection.Buy:
                    counters.Increment(RuntimeCounterKeys.TradeDirectionBuy);
                    break;
                case TradeDirection.Sell:
                    counters.Increment(RuntimeCounterKeys.TradeDirectionSell);
                    break;
                default:
                    counters.Increment(RuntimeCounterKeys.TradeDirectionUnknown);
                    IncrementUnknownReason(counters, marketData.TradeDirectionUnknownReason);
                    break;
            }

            if (marketData.TradeDirectionSource == TradeDirectionSource.ProviderSupplied)
            {
                counters.Increment(RuntimeCounterKeys.TradeDirectionProviderSupplied);
            }
        }

        private static void IncrementUnknownReason(RuntimeCounters counters, TradeDirectionUnknownReason reason)
        {
            switch (reason)
            {
                case TradeDirectionUnknownReason.PriceInsideSpread:
                    counters.Increment(RuntimeCounterKeys.TradeDirectionInsideSpread);
                    break;
                case TradeDirectionUnknownReason.NoBboAvailable:
                    counters.Increment(RuntimeCounterKeys.TradeDirectionNoBbo);
                    break;
                case TradeDirectionUnknownReason.BboIncomplete:
                    counters.Increment(RuntimeCounterKeys.TradeDirectionBboIncomplete);
                    break;
                case TradeDirectionUnknownReason.BboInvalid:
                    counters.Increment(RuntimeCounterKeys.TradeDirectionBboInvalid);
                    break;
                case TradeDirectionUnknownReason.ProviderReportedUnknown:
                    counters.Increment(RuntimeCounterKeys.TradeDirectionProviderUnknown);
                    break;
            }
        }
    }
}
