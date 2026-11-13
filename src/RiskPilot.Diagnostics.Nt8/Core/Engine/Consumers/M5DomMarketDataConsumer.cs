using RiskPilot.Diagnostics.Nt8.Core.Models;
using RiskPilot.Diagnostics.Nt8.Core.Session;

namespace RiskPilot.Diagnostics.Nt8.Core.Engine.Consumers
{
    /// <summary>
    /// M5 event consumer. Runs on the drain thread and accumulates raw DOM/market-data
    /// evidence: Level I quote/trade presence, the NT8 host-layer DOM operation counters and
    /// the last snapshot-sync trigger. It is read-only and never changes host state.
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
    }
}
