using System;
using System.Collections.Generic;
using RiskPilot.Diagnostics.Nt8.Core.Engine.Consumers;
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
            MergeStrategyRiskGate(hostSnapshot, session);
            MergeSecurityScan(hostSnapshot, session);
            return hostSnapshot;
        }

        /// <summary>
        /// Copies the session's security-scan result (owned and executed by M8) into the snapshot's
        /// safety observation, so the exported evidence reports the same single scan the live probe
        /// produced. It never re-runs the scan and never fabricates a result: with no scan on the
        /// session the safety observation is left as-is (M8 reports it INCONCLUSIVE).
        /// </summary>
        private static void MergeSecurityScan(DiagnosticSnapshot snapshot, SessionContext session)
        {
            var scan = session.SecurityScan;
            if (scan == null)
            {
                return;
            }

            var safety = snapshot.Safety;
            if (safety == null)
            {
                safety = new SafetyObservation();
                snapshot.Safety = safety;
            }

            safety.SecurityScan = scan;
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

            // A malformed depth event is itself evidence the host adapter was reached: the host
            // delivered an event we could not use. Counting it keeps the loss visible instead of
            // hiding it behind an "unobservable" surface.
            observation.MalformedCount += counters.Get(RuntimeCounterKeys.DomHostMalformed);

            // The host layer is observable once it reported any DOM activity or declared it so.
            host.Observable = host.Observable
                || host.InsertCount > 0 || host.UpdateCount > 0 || host.RemoveCount > 0
                || host.ResetCount > 0 || host.SnapshotSyncCount > 0
                || observation.MalformedCount > 0;

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
            observation.UnknownUnclassifiedCount += counters.Get(RuntimeCounterKeys.TradeDirectionUnclassified);

            var providerSupplied = counters.Get(RuntimeCounterKeys.TradeDirectionProviderSupplied);
            if (providerSupplied > 0)
            {
                observation.AnyProviderSuppliedDirection = true;
            }

            var classified = observation.BuyDerivedCount + observation.SellDerivedCount;
            observation.AllDirectionsQuoteDerived = !observation.AnyProviderSuppliedDirection && classified > 0;

            // The trade stream is observable only once a print was actually seen; until then a zero
            // unknown/gap count proves nothing.
            if (counters.Get(RuntimeCounterKeys.TradeStreamObserved) > 0)
            {
                observation.TradeStreamObservable = true;
            }

            if (counters.Get(RuntimeCounterKeys.TradeDeltaSequenceObserved) > 0)
            {
                observation.SequenceObservable = true;
            }

            var resetsMarketData = counters.Get(RuntimeCounterKeys.TradeDeltaResetMarketData);
            var resetsDepth = counters.Get(RuntimeCounterKeys.TradeDeltaResetDepth);
            observation.MarketDataResetCount += resetsMarketData;
            observation.DepthResetCount += resetsDepth;
            observation.ResetCount += resetsMarketData + resetsDepth;
            if (observation.ResetCount > 0)
            {
                observation.ResetObserved = true;
            }

            // The source-time basis is the weakest basis actually observed on the trade stream,
            // never assumed. The counter is basis+1 so an explicit Unknown (0) stays distinct from
            // "no observation"; with no observed trade record the basis is left UNKNOWN.
            var basisWorst = counters.Get(RuntimeCounterKeys.TradeDeltaSourceTimeBasisWorst);
            if (basisWorst > 0)
            {
                observation.SourceTimeBasis = (SourceTimeBasis)(basisWorst - 1);
            }

            // Per-domain ingest integrity for the order-flow trade stream.
            observation.SequenceGapCount += counters.Get(RuntimeCounterKeys.TradeDeltaSequenceGaps);

            DeriveTradeDeltaCompleteness(observation);
        }

        /// <summary>
        /// Derives the TradeDelta completeness from the observed trade stream when the host layer
        /// did not supply one. No public NinjaTrader API exposes TradeDelta completeness, so this is
        /// derived evidence, never a fabricated PASS: a sequence gap or any unclassified print makes
        /// it INCOMPLETE, and with no observed print it stays UNOBSERVABLE.
        /// </summary>
        private static void DeriveTradeDeltaCompleteness(TradeDeltaObservation observation)
        {
            if (!string.IsNullOrEmpty(observation.TradeDeltaCompleteness)
                && observation.TradeDeltaCompleteness != "UNOBSERVABLE")
            {
                // A value supplied by the caller is preserved verbatim; the probe corroborates it.
                return;
            }

            if (!observation.TradeStreamObservable)
            {
                observation.TradeDeltaCompleteness = "UNOBSERVABLE";
                return;
            }

            if (observation.SequenceGapCount > 0)
            {
                observation.TradeDeltaCompleteness = "INCOMPLETE";
                observation.TradeDeltaUnavailableReason = "SEQUENCE_GAP_DETECTED";
                return;
            }

            if (observation.UnknownUnclassifiedCount > 0)
            {
                observation.TradeDeltaCompleteness = "INCOMPLETE";
                observation.TradeDeltaUnavailableReason = "UNCLASSIFIED_TRADE_DIRECTION";
                return;
            }

            observation.TradeDeltaCompleteness = "COMPLETE";
        }

        private static void MergeStrategyRiskGate(DiagnosticSnapshot snapshot, SessionContext session)
        {
            var observation = snapshot.StrategyRiskGate;
            if (observation == null)
            {
                observation = new StrategyRiskGateObservation();
                snapshot.StrategyRiskGate = observation;
            }

            var accumulator = session.StrategyEvidence;
            if (accumulator != null && accumulator.ObservedRecordCount > 0)
            {
                // A real read-only source reported strategy records. The accumulator holds the
                // per-strategy detail and the reason-code distributions, so it is the source of
                // truth; merge it in rather than the flat counters.
                MergeAccumulated(observation, accumulator);
                return;
            }

            // No source reported strategy records this session. Preserve any per-strategy detail
            // the host snapshot already carried (for example a host adapter), and fold the flat
            // reason-code counters into the distributions so a counter-only path is still
            // reported. The two are combined, never overwritten.
            MergeReasonCodeDistribution(observation.CandidateRejectionReasons, session.Counters, StrategyReasonCodes.Stage1);
            MergeReasonCodeDistribution(observation.CandidateRejectionReasons, session.Counters, StrategyReasonCodes.Stage2);
            MergeReasonCodeDistribution(observation.Stage3ReasonCodes, session.Counters, StrategyReasonCodes.Stage3);
            MergeReasonCodeDistribution(observation.RiskGateReasonCodes, session.Counters, StrategyReasonCodes.Stage4);
            MergeUnattributedReasonCodes(observation, session.Counters);
        }

        private static void MergeAccumulated(StrategyRiskGateObservation observation, StrategyEvidenceAccumulator accumulator)
        {
            var accumulated = accumulator.ToObservation();

            foreach (var strategy in accumulated.Strategies)
            {
                observation.Strategies.Add(strategy);
            }

            if (accumulated.ObservedRecordCount > observation.ObservedRecordCount)
            {
                observation.ObservedRecordCount = accumulated.ObservedRecordCount;
            }

            AddDistributions(observation.CandidateRejectionReasons, accumulated.CandidateRejectionReasons);
            AddDistributions(observation.Stage3ReasonCodes, accumulated.Stage3ReasonCodes);
            AddDistributions(observation.RiskGateReasonCodes, accumulated.RiskGateReasonCodes);
            AddDistributions(observation.UnattributedReasonCodes, accumulated.UnattributedReasonCodes);

            if (accumulated.Correlation != null
                && (accumulated.Correlation.CorrelationObservable || observation.Correlation == null || !observation.Correlation.CorrelationObservable))
            {
                observation.Correlation = accumulated.Correlation;
            }
        }

        private static void AddDistributions(IDictionary<string, long> target, IDictionary<string, long> source)
        {
            if (target == null || source == null)
            {
                return;
            }

            foreach (var pair in source)
            {
                target.TryGetValue(pair.Key, out var current);
                target[pair.Key] = current + pair.Value;
            }
        }

        private static void MergeReasonCodeDistribution(
            IDictionary<string, long> distribution,
            RuntimeCounters counters,
            string stage)
        {
            if (distribution == null)
            {
                return;
            }

            foreach (var code in StrategyReasonCodes.CodesFor(stage))
            {
                var count = counters.Get(M7StrategyConsumer.ReasonCodePrefix + code);
                if (count <= 0)
                {
                    continue;
                }

                distribution.TryGetValue(code, out var current);
                distribution[code] = current + count;
            }
        }

        private static void MergeUnattributedReasonCodes(StrategyRiskGateObservation observation, RuntimeCounters counters)
        {
            foreach (var pair in counters.Snapshot())
            {
                if (pair.Value <= 0 || !pair.Key.StartsWith(M7StrategyConsumer.ReasonCodePrefix, StringComparison.Ordinal))
                {
                    continue;
                }

                var code = pair.Key.Substring(M7StrategyConsumer.ReasonCodePrefix.Length);
                if (string.IsNullOrEmpty(code) || !string.Equals(StrategyReasonCodes.StageOf(code), StrategyReasonCodes.Unattributed, StringComparison.Ordinal))
                {
                    continue;
                }

                // The code is either absent from the catalog or ambiguous across stages, so it
                // cannot be attributed to one stage. It is recorded once in the unattributed
                // bucket so the module reports the exact code rather than silently discarding it.
                observation.UnattributedReasonCodes.TryGetValue(code, out var current);
                observation.UnattributedReasonCodes[code] = current + pair.Value;
            }
        }
    }
}
