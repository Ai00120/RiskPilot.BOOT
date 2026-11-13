using RiskPilot.Diagnostics.Nt8.Core.Models;
using RiskPilot.Diagnostics.Nt8.Core.Session;

namespace RiskPilot.Diagnostics.Nt8.Core.Engine.Consumers
{
    /// <summary>
    /// M6 event consumer. Runs on the drain thread and accumulates raw trade-direction
    /// provenance from the observed trade stream: buy/sell/unknown counts, the exact
    /// unknown-direction cause, whether any direction was provider-supplied, whether the
    /// stream was observable at all, and whether a reset was seen on either feed.
    /// <para>
    /// NinjaTrader's public Level I surface carries no aggressor side, so a print that arrives
    /// unclassified is classified here by comparing it to the best bid/ask known at that moment
    /// (the BBO carried on the event, else the last observed quote). The result is always counted
    /// as *derived*, never provider-confirmed, and a print that cannot be classified keeps its
    /// explicit unknown reason instead of being guessed.
    /// </para>
    /// </summary>
    public sealed class M6TradeDeltaConsumer : INormalizedEventConsumer
    {
        private readonly SessionContext _session;

        // Best bid/ask retained from the last observed quote event, used only as a fallback when
        // a print does not carry its own BBO. This is host-reported data, never a fabricated value.
        private double? _lastBid;
        private double? _lastAsk;

        public M6TradeDeltaConsumer(SessionContext session)
        {
            _session = session;
        }

        public string Name { get { return "M6.TradeDelta"; } }

        public void Consume(NormalizedRecord record)
        {
            if (_session == null)
            {
                return;
            }

            var counters = _session.Counters;

            if (record is DomRecord dom)
            {
                // A Level II reset invalidates the derived order-flow/DOM mirror; it is a distinct
                // event from a Level I market-data reset and is counted separately.
                if (dom.IsReset || dom.Operation == DomOperation.Reset)
                {
                    counters.Increment(RuntimeCounterKeys.TradeDeltaResetDepth);
                }

                return;
            }

            if (!(record is MarketDataRecord marketData))
            {
                return;
            }

            if (marketData.IsReset)
            {
                counters.Increment(RuntimeCounterKeys.TradeDeltaResetMarketData);
            }

            if (marketData.Kind == MarketDataKind.Quote)
            {
                RetainQuote(marketData);
                return;
            }

            if (marketData.Kind != MarketDataKind.Trade)
            {
                return;
            }

            counters.Increment(RuntimeCounterKeys.TradeStreamObserved);

            // Continuity is only measurable when the source reported a per-stream sequence.
            if (marketData.SourceSequence.HasValue)
            {
                counters.Increment(RuntimeCounterKeys.TradeDeltaSequenceObserved);
            }

            // The trust basis of the trade stream is the weakest basis actually observed. Encoded
            // as basis+1 so an explicit Unknown (0) stays distinct from "no observation".
            counters.ObserveMin(RuntimeCounterKeys.TradeDeltaSourceTimeBasisWorst, (long)marketData.SourceTimeBasis + 1);

            var direction = marketData.TradeDirection;
            var source = marketData.TradeDirectionSource;
            var reason = marketData.TradeDirectionUnknownReason;

            // Classify only a print the host left entirely unclassified (no direction, no
            // provenance and no reason). A print that already carries a provenance or an explicit
            // unknown reason is counted exactly as reported, never re-derived.
            if (direction == TradeDirection.Unknown
                && source == TradeDirectionSource.Unspecified
                && reason == TradeDirectionUnknownReason.NotApplicable)
            {
                var bid = TradeDirectionClassifier.ToPositiveOrNull(marketData.Bid) ?? _lastBid;
                var ask = TradeDirectionClassifier.ToPositiveOrNull(marketData.Ask) ?? _lastAsk;

                TradeDirectionClassifier.Classify(marketData.Last, bid, ask, out var derived, out reason);
                if (derived.HasValue)
                {
                    direction = derived.Value;
                    source = TradeDirectionSource.DerivedFromQuote;
                }
            }

            switch (direction)
            {
                case TradeDirection.Buy:
                    counters.Increment(RuntimeCounterKeys.TradeDirectionBuy);
                    break;
                case TradeDirection.Sell:
                    counters.Increment(RuntimeCounterKeys.TradeDirectionSell);
                    break;
                default:
                    counters.Increment(RuntimeCounterKeys.TradeDirectionUnknown);
                    IncrementUnknownReason(counters, reason);
                    break;
            }

            if (source == TradeDirectionSource.ProviderSupplied)
            {
                counters.Increment(RuntimeCounterKeys.TradeDirectionProviderSupplied);
            }
        }

        private void RetainQuote(MarketDataRecord quote)
        {
            var bid = TradeDirectionClassifier.ToPositiveOrNull(quote.Bid);
            var ask = TradeDirectionClassifier.ToPositiveOrNull(quote.Ask);
            if (bid.HasValue)
            {
                _lastBid = bid;
            }

            if (ask.HasValue)
            {
                _lastAsk = ask;
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
                default:
                    // A print that carries no classification and no reason is counted as
                    // unclassified so the unknown total is always fully attributed.
                    counters.Increment(RuntimeCounterKeys.TradeDirectionUnclassified);
                    break;
            }
        }
    }
}

