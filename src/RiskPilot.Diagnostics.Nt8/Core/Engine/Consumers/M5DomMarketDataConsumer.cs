using System;
using RiskPilot.Diagnostics.Nt8.Core.Models;
using RiskPilot.Diagnostics.Nt8.Core.Session;

namespace RiskPilot.Diagnostics.Nt8.Core.Engine.Consumers
{
    /// <summary>
    /// M5 event consumer. Runs on the drain thread and accumulates raw DOM/market-data
    /// evidence: Level I quote/trade presence, the NT8 host-layer DOM operation counters, the
    /// validity counters (invalid price/size/position, malformed) and the last snapshot-sync
    /// trigger. It is read-only and never changes host state.
    /// <para>
    /// The recorder layer counters are a separate layer and are supplied by the recorder, not
    /// derived here; the two layers are never summed.
    /// </para>
    /// </summary>
    public sealed class M5DomMarketDataConsumer : INormalizedEventConsumer
    {
        private readonly SessionContext _session;

        public M5DomMarketDataConsumer(SessionContext session)
        {
            _session = session;
        }

        public string Name { get { return "M5.DomMarketData"; } }

        public void Consume(NormalizedRecord record)
        {
            if (_session == null || record == null)
            {
                return;
            }

            var counters = _session.Counters;

            if (record is DomRecord dom)
            {
                // The observation instant is the ingest time the pipeline stamped on the
                // record; the source timestamp is only a trust-labelled hint, so freshness is
                // derived from a value the tool actually observed.
                _session.RecordMarketDataEventUtc(dom.IngestedAtUtc);

                // A record we cannot attribute to the instrument or to a known depth operation
                // is malformed: it contributes no depth evidence and is counted so the loss is
                // visible rather than silently accepted.
                if (string.IsNullOrEmpty(dom.InstrumentId) || dom.Operation == DomOperation.Unknown)
                {
                    counters.Increment(RuntimeCounterKeys.DomHostMalformed);
                    return;
                }

                switch (dom.Operation)
                {
                    case DomOperation.Insert:
                        counters.Increment(RuntimeCounterKeys.DomHostInsert);
                        break;
                    case DomOperation.Update:
                        counters.Increment(RuntimeCounterKeys.DomHostUpdate);
                        break;
                    case DomOperation.Remove:
                        counters.Increment(RuntimeCounterKeys.DomHostRemove);
                        break;
                    case DomOperation.Reset:
                        counters.Increment(RuntimeCounterKeys.DomHostReset);
                        break;
                }

                // A reset signalled as a flag on a non-reset operation is still one reset;
                // counting it again for an explicit Reset operation would double-count.
                if (dom.IsReset && dom.Operation != DomOperation.Reset)
                {
                    counters.Increment(RuntimeCounterKeys.DomHostReset);
                }

                CountInvalidFields(dom, counters);
                return;
            }

            if (record is MarketDataRecord marketData)
            {
                _session.RecordMarketDataEventUtc(marketData.IngestedAtUtc);

                if (marketData.Kind == MarketDataKind.Quote)
                {
                    counters.Increment(RuntimeCounterKeys.QuoteObserved);
                }
                else if (marketData.Kind == MarketDataKind.Trade)
                {
                    counters.Increment(RuntimeCounterKeys.LastTradeObserved);
                }
            }
        }

        /// <summary>
        /// Counts a depth record whose price, size or ladder position is not a plausible value.
        /// A non-zero count is itself evidence that the depth mirror is incomplete, so it is
        /// reported rather than silently dropped. A reset carries no price/size/position and is
        /// not validated. Only values that can never be legitimate are flagged, so a legitimate
        /// host field (for example a zero price on a removal) is never reported as a failure.
        /// </summary>
        private static void CountInvalidFields(DomRecord dom, RuntimeCounters counters)
        {
            if (dom.Operation == DomOperation.Reset || dom.IsReset)
            {
                return;
            }

            if (double.IsNaN(dom.Price) || double.IsInfinity(dom.Price) || dom.Price < 0.0)
            {
                counters.Increment(RuntimeCounterKeys.DomHostInvalidPrice);
            }

            if (dom.Volume < 0)
            {
                counters.Increment(RuntimeCounterKeys.DomHostInvalidSize);
            }

            if (dom.Position < 0)
            {
                counters.Increment(RuntimeCounterKeys.DomHostInvalidPosition);
            }
        }
    }
}
