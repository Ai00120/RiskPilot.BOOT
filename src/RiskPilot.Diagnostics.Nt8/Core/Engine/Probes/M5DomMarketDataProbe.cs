using System;
using System.Collections.Generic;
using RiskPilot.Diagnostics.Nt8.Core.Models;
using RiskPilot.Diagnostics.Nt8.Core.Models.Observations;
using RiskPilot.Diagnostics.Nt8.Core.Result;
using RiskPilot.Diagnostics.Nt8.Core.Status;

namespace RiskPilot.Diagnostics.Nt8.Core.Engine.Probes
{
    /// <summary>
    /// M5 — DOM &amp; market data. Reports the depth-of-market quality signals from two
    /// independent counter layers (the NT8 host adapter and the recorder) that must never be
    /// combined, plus Level I quote/trade presence, Level II depth presence, snapshot
    /// synchronization, out-of-bounds and invalid-value counts, freshness and data-loss/gap
    /// signals.
    /// <para>
    /// Every figure is raw evidence. The module does not declare a counter "healthy" and does
    /// not compare against the historical baseline as if it were live data. A check that has
    /// no observed data — a missing stream, an absent age, an unreadable depth surface —
    /// stays INCONCLUSIVE, never PASS. A non-zero counter is itself evidence and fails.
    /// </para>
    /// </summary>
    public sealed class M5DomMarketDataProbe : IDiagnosticProbe
    {
        /// <summary>
        /// Age beyond which observed market data is reported as STALE. It is a reporting
        /// threshold derived from the expected sub-second tick cadence; it never turns an
        /// observation into a PASS, it only labels a data point as stale.
        /// </summary>
        public const double StaleAfterSeconds = 60.0;

        public string ModuleId { get { return "M5"; } }

        public string Title { get { return "DOM & Market Data"; } }

        public IReadOnlyList<DiagnosticResult> Evaluate(ProbeContext context)
        {
            var results = new List<DiagnosticResult>();
            var now = context.Clock.UtcNow;
            var correlation = context.CorrelationId;
            var snapshot = context.Snapshot;
            var observation = snapshot != null ? snapshot.DomMarketData : null;

            if (observation == null)
            {
                results.Add(FindingFactory.Unobservable(ModuleId, "M5.DOM_SURFACE", correlation, now,
                    "No DOM/market-data observation was supplied by the host layer.",
                    "Subscribe an instrument and run the pass while market data is flowing."));
                return results;
            }

            results.Add(BuildSurfaceCheck(observation, correlation, now));
            results.Add(BuildLevel1Check(observation, correlation, now));
            results.Add(BuildDepthCheck(observation, correlation, now));
            results.Add(BuildHostLayerCheck(observation.HostAdapter, correlation, now));
            results.Add(BuildRecorderLayerCheck(observation.Recorder, correlation, now));
            results.Add(BuildSnapshotSyncCheck(observation, correlation, now));
            results.Add(BuildValidityCheck(observation, correlation, now));
            results.Add(BuildFreshnessCheck(observation, correlation, now));
            results.Add(BuildIntegrityCheck(observation, correlation, now));

            return results;
        }

        /// <summary>
        /// Reports whether the host DOM/market-data surface was actually reachable at all.
        /// When nothing was observed the module says so explicitly rather than leaving the
        /// absence implicit in every derived check.
        /// </summary>
        private DiagnosticResult BuildSurfaceCheck(DomMarketDataObservation observation, string correlation, DateTime now)
        {
            if (!observation.DataSurfaceObservable)
            {
                return FindingFactory.Unobservable(ModuleId, "M5.DOM_SURFACE", correlation, now,
                    "No DOM/market-data surface was observable; no Level I, Level II or host-adapter evidence was supplied.",
                    "Subscribe the instrument (Level I and Level II) and re-run while market data is flowing.");
            }

            return DiagnosticResult
                .Pass(ModuleId, "M5.DOM_SURFACE", correlation, now)
                .WithFindingId("M5.DOM_SURFACE")
                .WithReasonCode("DOM_SURFACE_OBSERVED")
                .WithObservation("level1Quote=" + observation.Level1QuoteObserved
                    + " lastTrade=" + observation.LastTradeObserved
                    + " depth=" + observation.DepthObserved)
                .WithSource(FindingFactory.SourcePublicApi)
                .WithMethod("Instrument.MarketData (Level I) and Instrument.MarketDepth (Level II) subscriptions.")
                .WithLimitation("Presence is asserted only for events seen while the tool ran; a quiet market is not a failure.")
                .WithNextAction("None.")
                .WithEvidence(
                    DiagnosticEvidence.Observable(FindingFactory.SourcePublicApi, "level1QuoteObserved=" + observation.Level1QuoteObserved, now),
                    DiagnosticEvidence.Observable(FindingFactory.SourcePublicApi, "lastTradeObserved=" + observation.LastTradeObserved, now),
                    DiagnosticEvidence.Observable(FindingFactory.SourcePublicApi, "depthObserved=" + observation.DepthObserved, now));
        }

        private DiagnosticResult BuildLevel1Check(DomMarketDataObservation observation, string correlation, DateTime now)
        {
            var quote = observation.Level1QuoteObserved;
            var trade = observation.LastTradeObserved;
            var both = quote && trade;

            return (both
                    ? DiagnosticResult.Pass(ModuleId, "M5.LEVEL1", correlation, now)
                    : DiagnosticResult.Inconclusive(ModuleId, "M5.LEVEL1", correlation, now))
                .WithFindingId("M5.LEVEL1")
                .WithReasonCode(quote ? "QUOTE_OBSERVED" : "QUOTE_NOT_OBSERVED", trade ? "LAST_TRADE_OBSERVED" : "LAST_TRADE_NOT_OBSERVED")
                .WithObservation("quote=" + quote + " lastTrade=" + trade)
                .WithSource(FindingFactory.SourcePublicApi)
                .WithMethod("Instrument.MarketData.Update for quote and last-trade events.")
                .WithLimitation("Level I presence is asserted only for events seen while the tool ran.")
                .WithNextAction(both ? "None." : "Confirm the instrument is subscribed and market data is flowing.")
                .WithEvidence(
                    DiagnosticEvidence.Observable(FindingFactory.SourcePublicApi, "quote=" + quote, now),
                    DiagnosticEvidence.Observable(FindingFactory.SourcePublicApi, "lastTrade=" + trade, now));
        }

        /// <summary>
        /// Level II depth presence. Depth is a distinct stream from Level I; a Level I quote is
        /// never presented as depth. When no depth event was observed the check is
        /// INCONCLUSIVE, never PASS, because absence of depth is not proof of depth quality.
        /// </summary>
        private DiagnosticResult BuildDepthCheck(DomMarketDataObservation observation, string correlation, DateTime now)
        {
            var block = observation.HostAdapter;
            var depthOperations = block != null
                ? block.InsertCount + block.UpdateCount + block.RemoveCount + block.ResetCount
                : 0;
            var observed = observation.DepthObserved || depthOperations > 0;

            return (observed
                    ? DiagnosticResult.Pass(ModuleId, "M5.DEPTH", correlation, now)
                    : DiagnosticResult.Inconclusive(ModuleId, "M5.DEPTH", correlation, now))
                .WithFindingId("M5.DEPTH")
                .WithReasonCode(observed ? "DEPTH_OBSERVED" : "DEPTH_NOT_OBSERVED",
                    "DEPTH_OPERATIONS=" + depthOperations)
                .WithObservation("depthObserved=" + observed + " depthOperations=" + depthOperations)
                .WithSource(FindingFactory.SourcePublicApi)
                .WithMethod("Instrument.MarketDepth.Update depth operations (insert/update/remove/reset).")
                .WithLimitation("Depth presence is asserted only for events seen while the tool ran; it is a separate stream from Level I.")
                .WithNextAction(observed ? "None." : "Subscribe the DOM (Level II) and re-run while depth updates are arriving.")
                .WithEvidence(
                    DiagnosticEvidence.Observable(FindingFactory.SourcePublicApi, "depthObserved=" + observed, now),
                    DiagnosticEvidence.Observable(FindingFactory.SourcePublicApi, "depthOperations=" + depthOperations, now));
        }

        private DiagnosticResult BuildHostLayerCheck(DomCounterBlock block, string correlation, DateTime now)
        {
            if (block == null || !block.Observable)
            {
                return FindingFactory.Unobservable(ModuleId, "M5.HOST_LAYER_COUNTERS", correlation, now,
                    "The NT8 host DOM adapter counters were not observable.",
                    "Subscribe the DOM and re-run while depth updates are arriving.");
            }

            return DiagnosticResult
                .Pass(ModuleId, "M5.HOST_LAYER_COUNTERS", correlation, now)
                .WithFindingId("M5.HOST_LAYER_COUNTERS")
                .WithReasonCode("HOST_LAYER_OBSERVED", "LAYER=" + block.Layer)
                .WithObservation("insert=" + block.InsertCount + " update=" + block.UpdateCount + " remove=" + block.RemoveCount + " reset=" + block.ResetCount)
                .WithSource(FindingFactory.SourceRuntimeCounters)
                .WithMethod("Snapshot of the DOM operations observed on the ingest path for this layer.")
                .WithLimitation("This is the tool-side ingest layer only; the recorder layer is reported separately and the two must never be summed.")
                .WithNextAction("None.")
                .WithEvidence(
                    DiagnosticEvidence.Observable(FindingFactory.SourceRuntimeCounters, "insert=" + block.InsertCount, now),
                    DiagnosticEvidence.Observable(FindingFactory.SourceRuntimeCounters, "update=" + block.UpdateCount, now),
                    DiagnosticEvidence.Observable(FindingFactory.SourceRuntimeCounters, "remove=" + block.RemoveCount, now),
                    DiagnosticEvidence.Observable(FindingFactory.SourceRuntimeCounters, "reset=" + block.ResetCount, now),
                    DiagnosticEvidence.Observable(FindingFactory.SourceRuntimeCounters, "nativeSnapshotBids=" + Nullable(block.NativeSnapshotBids), now),
                    DiagnosticEvidence.Observable(FindingFactory.SourceRuntimeCounters, "nativeSnapshotAsks=" + Nullable(block.NativeSnapshotAsks), now),
                    DiagnosticEvidence.Observable(FindingFactory.SourceRuntimeCounters, "internalBidLevels=" + Nullable(block.InternalBidLevels), now),
                    DiagnosticEvidence.Observable(FindingFactory.SourceRuntimeCounters, "internalAskLevels=" + Nullable(block.InternalAskLevels), now));
        }

        private DiagnosticResult BuildRecorderLayerCheck(DomCounterBlock block, string correlation, DateTime now)
        {
            if (block == null || !block.Observable)
            {
                return DiagnosticResult
                    .NotApplicable(ModuleId, "M5.RECORDER_LAYER_COUNTERS", correlation, now)
                    .WithFindingId("M5.RECORDER_LAYER_COUNTERS")
                    .WithReasonCode("RECORDER_LAYER_NOT_IN_SCOPE")
                    .WithObservation("The recorder layer counters were not supplied to this pass.")
                    .WithSource(FindingFactory.SourceRuntimeCounters)
                    .WithMethod("Recorder layer counters are a separate layer and are only reported when supplied.")
                    .WithLimitation("Absence of the recorder layer is not a failure; it is a separate layer that may not be in scope.")
                    .WithNextAction("Supply the recorder layer counters if the recording pipeline is in scope.");
            }

            return DiagnosticResult
                .Pass(ModuleId, "M5.RECORDER_LAYER_COUNTERS", correlation, now)
                .WithFindingId("M5.RECORDER_LAYER_COUNTERS")
                .WithReasonCode("RECORDER_LAYER_OBSERVED", "LAYER=" + block.Layer)
                .WithObservation("update=" + block.UpdateCount + " insert=" + block.InsertCount + " remove=" + block.RemoveCount + " reset=" + block.ResetCount)
                .WithSource(FindingFactory.SourceRuntimeCounters)
                .WithMethod("Snapshot of the recorder layer DOM operation counters.")
                .WithLimitation("This is the recorder layer only; it is a different layer from the NT8 host adapter and the two must never be summed.")
                .WithNextAction("None.")
                .WithEvidence(
                    DiagnosticEvidence.Observable(FindingFactory.SourceRuntimeCounters, "recorderUpdate=" + block.UpdateCount, now),
                    DiagnosticEvidence.Observable(FindingFactory.SourceRuntimeCounters, "recorderInsert=" + block.InsertCount, now),
                    DiagnosticEvidence.Observable(FindingFactory.SourceRuntimeCounters, "recorderRemove=" + block.RemoveCount, now),
                    DiagnosticEvidence.Observable(FindingFactory.SourceRuntimeCounters, "recorderReset=" + block.ResetCount, now));
        }

        private DiagnosticResult BuildSnapshotSyncCheck(DomMarketDataObservation observation, string correlation, DateTime now)
        {
            var block = observation.HostAdapter;
            if (block == null || !block.Observable)
            {
                return FindingFactory.Unobservable(ModuleId, "M5.SNAPSHOT_SYNC", correlation, now,
                    "Snapshot synchronization counters were not observable.",
                    "Subscribe the DOM and re-run while depth updates are arriving.");
            }

            var sync = block.SnapshotSyncCount;
            var failure = block.SnapshotSyncFailureCount;
            var outOfBounds = block.InsertOutOfBoundsCount + block.UpdateOutOfBoundsCount + block.RemoveOutOfBoundsCount;

            // A sync failure is an explicit, counted failure: FAIL. Out-of-bounds operations are
            // a real mirror-drift signal even without a counted sync failure, so they FAIL too —
            // an index violation is never reported as a clean pass. With no sync evidence at all
            // the check is INCONCLUSIVE, never PASS.
            var status = (failure > 0 || outOfBounds > 0) ? DiagnosticStatus.FAIL
                : (sync == 0 ? DiagnosticStatus.INCONCLUSIVE : DiagnosticStatus.PASS);
            var result = status == DiagnosticStatus.FAIL
                ? DiagnosticResult.Fail(ModuleId, "M5.SNAPSHOT_SYNC", ErrorClass.DataUnavailable, correlation, now)
                : (status == DiagnosticStatus.INCONCLUSIVE
                    ? DiagnosticResult.Inconclusive(ModuleId, "M5.SNAPSHOT_SYNC", correlation, now)
                    : DiagnosticResult.Pass(ModuleId, "M5.SNAPSHOT_SYNC", correlation, now));

            var reason = failure > 0 ? "SNAPSHOT_SYNC_FAILURE"
                : (outOfBounds > 0 ? "SNAPSHOT_OUT_OF_BOUNDS"
                    : (sync == 0 ? "SNAPSHOT_SYNC_NOT_OBSERVED" : "SNAPSHOT_SYNC_CLEAN"));

            return result
                .WithFindingId("M5.SNAPSHOT_SYNC")
                .WithReasonCode(
                    reason,
                    "SYNC=" + sync,
                    "SYNC_FAILURE=" + failure,
                    "OUT_OF_BOUNDS_TOTAL=" + outOfBounds,
                    "INSERT_OOB=" + block.InsertOutOfBoundsCount,
                    "UPDATE_OOB=" + block.UpdateOutOfBoundsCount,
                    "REMOVE_OOB=" + block.RemoveOutOfBoundsCount)
                .WithObservation("sync=" + sync + " failure=" + failure + " outOfBounds=" + outOfBounds
                    + " trigger=" + (string.IsNullOrEmpty(observation.LastSnapshotSyncTrigger) ? "none" : observation.LastSnapshotSyncTrigger))
                .WithSource(FindingFactory.SourceRuntimeCounters)
                .WithMethod("Counted native-snapshot synchronizations and their out-of-bounds triggers.")
                .WithLimitation("Out-of-bounds operations are raw evidence of mirror drift, not by themselves proof of lost events.")
                .WithNextAction(failure > 0
                    ? "Investigate the failed snapshot synchronizations."
                    : (outOfBounds > 0
                        ? "Investigate the out-of-bounds depth operations; the mirror drifted from the native snapshot."
                        : (sync == 0 ? "Observe a longer window so snapshot sync is exercised." : "None.")))
                .WithEvidence(
                    DiagnosticEvidence.Observable(FindingFactory.SourceRuntimeCounters, "snapshotSync=" + sync, now),
                    DiagnosticEvidence.Observable(FindingFactory.SourceRuntimeCounters, "snapshotSyncFailure=" + failure, now),
                    DiagnosticEvidence.Observable(FindingFactory.SourceRuntimeCounters, "insertOob=" + block.InsertOutOfBoundsCount, now),
                    DiagnosticEvidence.Observable(FindingFactory.SourceRuntimeCounters, "updateOob=" + block.UpdateOutOfBoundsCount, now),
                    DiagnosticEvidence.Observable(FindingFactory.SourceRuntimeCounters, "removeOob=" + block.RemoveOutOfBoundsCount, now));
        }

        private DiagnosticResult BuildValidityCheck(DomMarketDataObservation observation, string correlation, DateTime now)
        {
            var block = observation.HostAdapter;
            if (block == null || !block.Observable)
            {
                return FindingFactory.Unobservable(ModuleId, "M5.VALIDITY", correlation, now,
                    "DOM validity counters were not observable.",
                    "Subscribe the DOM and re-run while depth updates are arriving.");
            }

            var invalid = block.InvalidPriceCount + block.InvalidSizeCount + block.InvalidPositionCount;
            var malformed = observation.MalformedCount;

            return (invalid == 0 && malformed == 0
                    ? DiagnosticResult.Pass(ModuleId, "M5.VALIDITY", correlation, now)
                    : DiagnosticResult.Fail(ModuleId, "M5.VALIDITY", ErrorClass.DataUnavailable, correlation, now))
                .WithFindingId("M5.VALIDITY")
                .WithReasonCode(
                    invalid == 0 && malformed == 0 ? "NO_INVALID_EVENTS" : "INVALID_EVENTS_OBSERVED",
                    "INVALID_PRICE=" + block.InvalidPriceCount,
                    "INVALID_SIZE=" + block.InvalidSizeCount,
                    "INVALID_POSITION=" + block.InvalidPositionCount,
                    "MALFORMED=" + malformed)
                .WithObservation("invalidPrice=" + block.InvalidPriceCount + " invalidSize=" + block.InvalidSizeCount
                    + " invalidPosition=" + block.InvalidPositionCount + " malformed=" + malformed)
                .WithSource(FindingFactory.SourceRuntimeCounters)
                .WithMethod("Counted DOM events rejected for an invalid price, size or position, and malformed events.")
                .WithLimitation("Invalid events are counted, never silently dropped; a non-zero count means depth evidence is incomplete.")
                .WithNextAction(invalid == 0 && malformed == 0
                    ? "None."
                    : "Inspect the invalid/malformed DOM events; depth evidence is incomplete until they are understood.")
                .WithEvidence(
                    DiagnosticEvidence.Observable(FindingFactory.SourceRuntimeCounters, "invalidPrice=" + block.InvalidPriceCount, now),
                    DiagnosticEvidence.Observable(FindingFactory.SourceRuntimeCounters, "invalidSize=" + block.InvalidSizeCount, now),
                    DiagnosticEvidence.Observable(FindingFactory.SourceRuntimeCounters, "invalidPosition=" + block.InvalidPositionCount, now),
                    DiagnosticEvidence.Observable(FindingFactory.SourceRuntimeCounters, "malformed=" + malformed, now));
        }

        /// <summary>
        /// Data freshness. The age is derived from the most recent event the tool actually
        /// observed (via the session's recorded last-event instant), never fabricated. With no
        /// observed event the age is unobservable; a stale age is reported as STALE so the
        /// operator can see why a reading is not fresh, and is never turned into a PASS.
        /// </summary>
        private DiagnosticResult BuildFreshnessCheck(DomMarketDataObservation observation, string correlation, DateTime now)
        {
            double? age = observation.LastDomEventAgeSeconds;
            if (!age.HasValue && observation.LastEventUtc.HasValue)
            {
                age = (now - observation.LastEventUtc.Value.ToUniversalTime()).TotalSeconds;
                if (age < 0)
                {
                    // The observed event is in the future relative to the tool clock; the age
                    // cannot be trusted, so it stays unobservable rather than reported negative.
                    age = null;
                }
            }

            if (!age.HasValue)
            {
                return FindingFactory.Unobservable(ModuleId, "M5.FRESHNESS", correlation, now,
                    "No market-data/DOM event was observed, so data freshness could not be evaluated.",
                    "Subscribe the instrument and re-run while market data is flowing.");
            }

            var stale = age.Value > StaleAfterSeconds;
            var ageText = age.Value.ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
            var staleText = StaleAfterSeconds.ToString("F1", System.Globalization.CultureInfo.InvariantCulture);

            return DiagnosticResult
                .Inconclusive(ModuleId, "M5.FRESHNESS", correlation, now)
                .WithFindingId("M5.FRESHNESS")
                .WithReasonCode(stale ? "STALE" : "FRESH", "LAST_EVENT_AGE_SECONDS=" + ageText, "STALE_AFTER_SECONDS=" + staleText)
                .WithObservation("lastDomEventAgeSeconds=" + ageText + " stale=" + stale)
                .WithSource(FindingFactory.SourceRuntimeCounters)
                .WithMethod("Computed the age of the last observed market-data/DOM event from its ingest timestamp and the tool clock.")
                .WithLimitation("Freshness is a point-in-time age; it is not a verdict, and it depends on the tool's own clock. A stale reading means the market-data window is not fresh, not that the data is wrong.")
                .WithNextAction(stale
                    ? "The last observed market-data event is older than " + staleText + "s; confirm market data is still flowing before relying on this window."
                    : "Correlate the age with the expected update cadence for the instrument.")
                .WithEvidence(
                    DiagnosticEvidence.Observable(FindingFactory.SourceRuntimeCounters, "lastDomEventAgeSeconds=" + ageText, now),
                    DiagnosticEvidence.Observable(FindingFactory.SourceRuntimeCounters, "stale=" + stale, now));
        }

        private DiagnosticResult BuildIntegrityCheck(DomMarketDataObservation observation, string correlation, DateTime now)
        {
            var gaps = observation.SequenceGapCount;
            var dropped = observation.DroppedEventCount;

            // A positive gap/drop count is itself evidence of a problem, so it fails regardless.
            // A zero count is only meaningful once the market-data/DOM stream was actually
            // observed: with no observed stream there is nothing to have been continuous, so the
            // check stays INCONCLUSIVE rather than reporting a clean PASS on absent data.
            if (gaps == 0 && dropped == 0 && !observation.DataSurfaceObservable)
            {
                return FindingFactory.Unobservable(ModuleId, "M5.DATA_INTEGRITY", correlation, now,
                    "No market-data/DOM stream was observed, so sequence continuity could not be evaluated.",
                    "Subscribe an instrument and run the pass while market data is flowing.");
            }

            var clean = gaps == 0 && dropped == 0;
            return (clean
                    ? DiagnosticResult.Pass(ModuleId, "M5.DATA_INTEGRITY", correlation, now)
                    : DiagnosticResult.Fail(ModuleId, "M5.DATA_INTEGRITY", ErrorClass.SequenceGap, correlation, now))
                .WithFindingId("M5.DATA_INTEGRITY")
                .WithReasonCode(
                    clean ? "NO_GAPS_OR_DROPS" : "SEQUENCE_OR_DROP_DETECTED",
                    "SEQUENCE_GAPS=" + gaps,
                    "DROPPED=" + dropped)
                .WithObservation("sequenceGaps=" + gaps + " dropped=" + dropped)
                .WithSource(FindingFactory.SourceRuntimeCounters)
                .WithMethod("Per-stream sequence-gap detection and bounded-queue drop counting on the ingest path.")
                .WithLimitation("A gap means the source skipped events; depth evidence is incomplete for that window.")
                .WithNextAction(clean
                    ? "None."
                    : "Treat the affected window as incomplete; increase queue capacity or reduce ingest rate.")
                .WithEvidence(
                    DiagnosticEvidence.Observable(FindingFactory.SourceRuntimeCounters, "sequenceGaps=" + gaps, now),
                    DiagnosticEvidence.Observable(FindingFactory.SourceRuntimeCounters, "dropped=" + dropped, now));
        }

        private static string Nullable(int? value)
        {
            return value.HasValue ? value.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "unavailable";
        }
    }
}
