using System;
using System.Collections.Generic;
using RiskPilot.Diagnostics.Nt8.Core.Models;
using RiskPilot.Diagnostics.Nt8.Core.Models.Observations;
using RiskPilot.Diagnostics.Nt8.Core.Session;

namespace RiskPilot.Diagnostics.Nt8.Core.Engine
{
    /// <summary>
    /// Merges the host-supplied observation snapshot (read from public NinjaTrader APIs)
    /// with the counter-derived evidence accumulated by the M3–M7 event consumers on the
    /// drain thread. Keeping the counter→observation mapping here means the host layer only
    /// reads host APIs and the counter logic is testable in the core.
    /// <para>
    /// A null host snapshot yields null: no observation could be made, and the probes report
    /// INCONCLUSIVE rather than a partial guess.
    /// </para>
    /// </summary>
    public static class SnapshotAssembler
    {
        public static DiagnosticSnapshot Assemble(DiagnosticSnapshot hostSnapshot, SessionContext session)
        {
            if (hostSnapshot == null)
            {
                return null;
            }

            if (session == null || session.Counters == null)
            {
                return hostSnapshot;
            }

            var counters = session.Counters;
            MergeOrderLifecycle(hostSnapshot, counters);
            MergeDomMarketData(hostSnapshot, session);
            MergeTradeDelta(hostSnapshot, counters);
            MergeStrategyRiskGate(hostSnapshot, counters);
            return hostSnapshot;
        }

        private static void AddClass(IDictionary<OrderIdClass, int> counts, OrderIdClass key, int delta)
        {
            if (delta == 0)
            {
                return;
            }

            counts.TryGetValue(key, out var current);
            counts[key] = current + delta;
        }

        private static void MergeOrderLifecycle(DiagnosticSnapshot snapshot, RuntimeCounters counters)
        {
            var observation = snapshot.OrderLifecycle;
            if (observation == null)
            {
                observation = new OrderLifecycleObservation();
                snapshot.OrderLifecycle = observation;
            }

            // The host snapshot carries point-in-time counts; the counters carry the event
            // stream observed on the drain thread. The two are added, never overwritten, so a
            // host-only snapshot (no event stream) still reports its evidence.
            observation.OrderSnapshotCount += (int)counters.Get(RuntimeCounterKeys.OrderSnapshot);
            observation.OrderUpdateCount += (int)counters.Get(RuntimeCounterKeys.OrderUpdate);
            observation.ExecutionUpdateCount += (int)counters.Get(RuntimeCounterKeys.ExecutionUpdate);
            observation.PositionUpdateCount += (int)counters.Get(RuntimeCounterKeys.PositionUpdate);
            observation.TerminalStateRetainedCount += (int)counters.Get(RuntimeCounterKeys.OrderTerminalRetained);

            if (observation.OrderIdClassCounts == null)
            {
                observation.OrderIdClassCounts = new Dictionary<OrderIdClass, int>();
            }

            AddClass(observation.OrderIdClassCounts, OrderIdClass.Null, (int)counters.Get(RuntimeCounterKeys.OrderIdNull));
            AddClass(observation.OrderIdClassCounts, OrderIdClass.Empty, (int)counters.Get(RuntimeCounterKeys.OrderIdEmpty));
            AddClass(observation.OrderIdClassCounts, OrderIdClass.Whitespace, (int)counters.Get(RuntimeCounterKeys.OrderIdWhitespace));
            AddClass(observation.OrderIdClassCounts, OrderIdClass.Valid, (int)counters.Get(RuntimeCounterKeys.OrderIdValid));
            AddClass(observation.OrderIdClassCounts, OrderIdClass.Unavailable, (int)counters.Get(RuntimeCounterKeys.OrderIdUnavailable));
        }

        private static void MergeDomMarketData(DiagnosticSnapshot snapshot, SessionContext session)
        {
            var counters = session.Counters;
            var observation = snapshot.DomMarketData;
            if (observation == null)
            {
                observation = new DomMarketDataObservation();
                snapshot.DomMarketData = observation;
            }

            var host = observation.HostAdapter ?? new DomCounterBlock { Layer = "HOST_ADAPTER" };
            host.Layer = "HOST_ADAPTER";
            host.InsertCount += counters.Get(RuntimeCounterKeys.DomHostInsert);
            host.UpdateCount += counters.Get(RuntimeCounterKeys.DomHostUpdate);
            host.RemoveCount += counters.Get(RuntimeCounterKeys.DomHostRemove);
            host.ResetCount += counters.Get(RuntimeCounterKeys.DomHostReset);
            host.SnapshotSyncCount += counters.Get(RuntimeCounterKeys.DomHostSnapshotSync);
            host.SnapshotSyncFailureCount += counters.Get(RuntimeCounterKeys.DomHostSnapshotSyncFailure);
            host.InsertOutOfBoundsCount += counters.Get(RuntimeCounterKeys.DomHostInsertOob);
            host.UpdateOutOfBoundsCount += counters.Get(RuntimeCounterKeys.DomHostUpdateOob);
            host.RemoveOutOfBoundsCount += counters.Get(RuntimeCounterKeys.DomHostRemoveOob);
            host.InvalidPriceCount += counters.Get(RuntimeCounterKeys.DomHostInvalidPrice);
            host.InvalidSizeCount += counters.Get(RuntimeCounterKeys.DomHostInvalidSize);
            host.InvalidPositionCount += counters.Get(RuntimeCounterKeys.DomHostInvalidPosition);

            // The host layer is observable once it reported any DOM activity or declared it so.
            host.Observable = host.Observable
                || host.InsertCount > 0 || host.UpdateCount > 0 || host.RemoveCount > 0
                || host.ResetCount > 0 || host.SnapshotSyncCount > 0;

            observation.HostAdapter = host;

            // Depth (Level II) is observed only from DOM records; a Level I quote is a
            // different stream and must never be presented as depth evidence.
            var depthOperations = host.InsertCount + host.UpdateCount + host.RemoveCount + host.ResetCount;
            if (depthOperations > 0)
            {
                observation.DepthObserved = true;
            }

            if (counters.Get(RuntimeCounterKeys.QuoteObserved) > 0)
            {
                observation.Level1QuoteObserved = true;
            }

            if (counters.Get(RuntimeCounterKeys.LastTradeObserved) > 0)
            {
                observation.LastTradeObserved = true;
            }

            observation.MalformedCount += counters.Get(RuntimeCounterKeys.DomHostMalformed);

            // Freshness is derived from the newest event the ingest path actually observed; it
            // is never fabricated, and stays absent until an event is seen.
            var lastEventUtc = session.LastMarketDataEventUtc;
            if (lastEventUtc.HasValue)
            {
                observation.LastEventUtc = lastEventUtc;
            }

            // A surface is observable only once a stream event was seen or the host adapter
            // declared its counters. Until then derived checks must stay INCONCLUSIVE.
            observation.DataSurfaceObservable = observation.DataSurfaceObservable
                || observation.Level1QuoteObserved
                || observation.LastTradeObserved
                || observation.DepthObserved
                || host.Observable;

            // Per-domain ingest integrity for the market-data/DOM domain. The counter is
            // attributed by stream on the ingest path, so it is the DOM/market-data share of
            // the gaps and drops, not a merged total.
            observation.SequenceGapCount += counters.Get(RuntimeCounterKeys.MarketDataSequenceGaps);
            observation.DroppedEventCount += counters.Get(RuntimeCounterKeys.MarketDataDroppedEvents);
        }

        private static void MergeTradeDelta(DiagnosticSnapshot snapshot, RuntimeCounters counters)
        {
            var observation = snapshot.TradeDelta;
            if (observation == null)
            {
                observation = new TradeDeltaObservation();
                snapshot.TradeDelta = observation;
            }

            observation.BuyDerivedCount += counters.Get(RuntimeCounterKeys.TradeDirectionBuy);
            observation.SellDerivedCount += counters.Get(RuntimeCounterKeys.TradeDirectionSell);
            observation.UnknownCount += counters.Get(RuntimeCounterKeys.TradeDirectionUnknown);
            observation.UnknownInsideSpreadCount += counters.Get(RuntimeCounterKeys.TradeDirectionInsideSpread);
            observation.UnknownNoBboCount += counters.Get(RuntimeCounterKeys.TradeDirectionNoBbo);
            observation.UnknownBboIncompleteCount += counters.Get(RuntimeCounterKeys.TradeDirectionBboIncomplete);
            observation.UnknownBboInvalidCount += counters.Get(RuntimeCounterKeys.TradeDirectionBboInvalid);
            observation.UnknownProviderReportedCount += counters.Get(RuntimeCounterKeys.TradeDirectionProviderUnknown);

            var providerSupplied = counters.Get(RuntimeCounterKeys.TradeDirectionProviderSupplied);
            if (providerSupplied > 0)
            {
                observation.AnyProviderSuppliedDirection = true;
            }

            var classified = observation.BuyDerivedCount + observation.SellDerivedCount;
            observation.AllDirectionsQuoteDerived = !observation.AnyProviderSuppliedDirection && classified > 0;

            var resets = counters.Get(RuntimeCounterKeys.TradeDeltaResetObserved);
            if (resets > 0)
            {
                observation.ResetObserved = true;
                observation.ResetCount += resets;
            }

            // Per-domain ingest integrity for the order-flow trade stream.
            observation.SequenceGapCount += counters.Get(RuntimeCounterKeys.TradeDeltaSequenceGaps);
        }

        private static void MergeStrategyRiskGate(DiagnosticSnapshot snapshot, RuntimeCounters counters)
        {
            var observation = snapshot.StrategyRiskGate;
            if (observation == null)
            {
                observation = new StrategyRiskGateObservation();
                snapshot.StrategyRiskGate = observation;
            }

            observation.AllCandidatesInvalidCount += counters.Get(RuntimeCounterKeys.StrategyAllCandidatesInvalid);
        }
    }
}
