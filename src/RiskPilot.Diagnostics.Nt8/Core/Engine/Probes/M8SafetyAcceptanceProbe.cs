using System;
using System.Collections.Generic;
using RiskPilot.Diagnostics.Nt8.Core.Identity;
using RiskPilot.Diagnostics.Nt8.Core.Models;
using RiskPilot.Diagnostics.Nt8.Core.Models.Observations;
using RiskPilot.Diagnostics.Nt8.Core.Result;
using RiskPilot.Diagnostics.Nt8.Core.Status;

namespace RiskPilot.Diagnostics.Nt8.Core.Engine.Probes
{
    /// <summary>
    /// M8 — Safety &amp; acceptance. Verifies the read-only posture from runtime evidence, not
    /// from a configuration value: <c>Trading=Disabled</c> in a settings file is not sufficient
    /// on its own. The module reports the runtime evidence for trading-disabled, the static
    /// proof that no order/account API exists in the tool, the secret-free state of the report,
    /// and the items that remain unobservable. Missing runtime evidence is INCONCLUSIVE, never
    /// PASS.
    /// </summary>
    public sealed class M8SafetyAcceptanceProbe : IDiagnosticProbe
    {
        public string ModuleId { get { return "M8"; } }

        public string Title { get { return "Safety / Acceptance"; } }

        public IReadOnlyList<DiagnosticResult> Evaluate(ProbeContext context)
        {
            var results = new List<DiagnosticResult>();
            var now = context.Clock.UtcNow;
            var correlation = context.CorrelationId;
            var snapshot = context.Snapshot;
            var observation = snapshot != null ? snapshot.Safety : null;

            if (observation == null)
            {
                results.Add(FindingFactory.Unobservable(ModuleId, "M8.TRADING_DISABLED", correlation, now,
                    "No safety observation was supplied by the host layer.",
                    "Run the pass inside the host so the runtime safety evidence is captured."));
                return results;
            }

            results.Add(BuildTradingDisabledCheck(observation, correlation, now));
            results.Add(BuildReadOnlyCheck(observation, correlation, now));
            results.Add(BuildStaticScanCheck(observation, correlation, now));
            results.Add(BuildSecretFreeCheck(observation, correlation, now));
            results.Add(BuildEvidenceIntegrityCheck(context, correlation, now));
            results.Add(BuildUnobservableCheck(observation, correlation, now));

            return results;
        }

        private DiagnosticResult BuildTradingDisabledCheck(SafetyObservation observation, string correlation, DateTime now)
        {
            if (!observation.TradingDisabledRuntimeEvidence)
            {
                // A configuration value is not enough; without runtime evidence the check is
                // INCONCLUSIVE, never PASS.
                return DiagnosticResult
                    .Inconclusive(ModuleId, "M8.TRADING_DISABLED", correlation, now)
                    .WithFindingId("M8.TRADING_DISABLED")
                    .WithReasonCode("TRADING_DISABLED_RUNTIME_EVIDENCE_MISSING")
                    .WithObservation("No runtime evidence that trading is disabled was captured.")
                    .WithSource(FindingFactory.SourcePublicApi)
                    .WithMethod("Runtime check of the tool's own trading-disabled invariant.")
                    .WithLimitation("A Trading=Disabled value in a settings file is NOT runtime evidence and is never accepted as PASS.")
                    .WithNextAction("Run the pass inside the host so the runtime trading-disabled evidence is captured.")
                    .WithEvidence(DiagnosticEvidence.Unobservable(FindingFactory.SourcePublicApi, "TRADING_DISABLED_RUNTIME_EVIDENCE_MISSING", now));
            }

            return DiagnosticResult
                .Pass(ModuleId, "M8.TRADING_DISABLED", correlation, now)
                .WithFindingId("M8.TRADING_DISABLED")
                .WithReasonCode("TRADING_DISABLED_RUNTIME_EVIDENCE_PRESENT")
                .WithObservation("Runtime evidence confirms the tool has no path that enables trading.")
                .WithSource(FindingFactory.SourcePublicApi)
                .WithMethod("Runtime inspection of the tool's own trading-disabled invariant (not a settings-file read).")
                .WithLimitation("This proves the tool's posture; it does not prove the state of any other component.")
                .WithNextAction("None.")
                .WithEvidence(DiagnosticEvidence.Observable(FindingFactory.SourcePublicApi, "tradingDisabledRuntimeEvidence=true", now));
        }

        private DiagnosticResult BuildReadOnlyCheck(SafetyObservation observation, string correlation, DateTime now)
        {
            if (!observation.ReadOnlyPostureObserved)
            {
                return DiagnosticResult
                    .Inconclusive(ModuleId, "M8.READ_ONLY_POSTURE", correlation, now)
                    .WithFindingId("M8.READ_ONLY_POSTURE")
                    .WithReasonCode("READ_ONLY_POSTURE_NOT_OBSERVED")
                    .WithObservation("The read-only posture was not confirmed at runtime.")
                    .WithSource(FindingFactory.SourcePublicApi)
                    .WithMethod("Runtime check that no order/account/connection mutation path is reachable.")
                    .WithLimitation("Absence of mutation is only proven for the paths the tool exercises.")
                    .WithNextAction("Run the pass inside the host; the static scan complements this check.");
            }

            return DiagnosticResult
                .Pass(ModuleId, "M8.READ_ONLY_POSTURE", correlation, now)
                .WithFindingId("M8.READ_ONLY_POSTURE")
                .WithReasonCode("READ_ONLY_POSTURE_OBSERVED")
                .WithObservation("No order/account/connection mutation path is reachable from the tool.")
                .WithSource(FindingFactory.SourcePublicApi)
                .WithMethod("Runtime check plus the static scan of production source.")
                .WithLimitation("Runtime observation is complemented by, but not replaced by, the static scan.")
                .WithNextAction("None.")
                .WithEvidence(DiagnosticEvidence.Observable(FindingFactory.SourcePublicApi, "readOnlyPostureObserved=true", now));
        }

        private DiagnosticResult BuildStaticScanCheck(SafetyObservation observation, string correlation, DateTime now)
        {
            if (!observation.StaticScanPassed)
            {
                return DiagnosticResult
                    .Inconclusive(ModuleId, "M8.STATIC_SAFETY_SCAN", correlation, now)
                    .WithFindingId("M8.STATIC_SAFETY_SCAN")
                    .WithReasonCode("STATIC_SCAN_NOT_SUPPLIED")
                    .WithObservation("No static safety scan result was supplied to this pass.")
                    .WithSource(FindingFactory.SourceStaticScan)
                    .WithMethod("The verification/addon-smoke static scan is run separately and its result is supplied here.")
                    .WithLimitation("Without the scan result, the absence of forbidden APIs is not asserted by this module.")
                    .WithNextAction("Run python3 verification/addon-smoke/test_diagnostic_safety.py and supply its result.");
            }

            return DiagnosticResult
                .Pass(ModuleId, "M8.STATIC_SAFETY_SCAN", correlation, now)
                .WithFindingId("M8.STATIC_SAFETY_SCAN")
                .WithReasonCode("STATIC_SCAN_PASSED", "FORBIDDEN_API_HITS=" + observation.StaticScanForbiddenApiCount, "FILES_SCANNED=" + observation.StaticScanFileCount)
                .WithObservation("forbiddenApiHits=" + observation.StaticScanForbiddenApiCount + " filesScanned=" + observation.StaticScanFileCount)
                .WithSource(FindingFactory.SourceStaticScan)
                .WithMethod("Static scan of production source for forbidden order/account/reflection/unsafe patterns.")
                .WithLimitation("A static scan proves the absence of forbidden patterns in source; it is not a runtime guarantee.")
                .WithNextAction("None.")
                .WithEvidence(
                    DiagnosticEvidence.Observable(FindingFactory.SourceStaticScan, "forbiddenApiHits=" + observation.StaticScanForbiddenApiCount, now),
                    DiagnosticEvidence.Observable(FindingFactory.SourceStaticScan, "filesScanned=" + observation.StaticScanFileCount, now));
        }

        private DiagnosticResult BuildSecretFreeCheck(SafetyObservation observation, string correlation, DateTime now)
        {
            if (!observation.ReportSecretFree)
            {
                return DiagnosticResult
                    .Inconclusive(ModuleId, "M8.SECRET_FREE_REPORT", correlation, now)
                    .WithFindingId("M8.SECRET_FREE_REPORT")
                    .WithReasonCode("SECRET_FREE_NOT_CONFIRMED")
                    .WithObservation("The report was not confirmed secret-free for this pass.")
                    .WithSource(FindingFactory.SourceRuntimeCounters)
                    .WithMethod("Re-scan of the serialized report for account identifiers, credentials and operator paths.")
                    .WithLimitation("Redaction is pattern-based; an unrecognised secret format could pass.")
                    .WithNextAction("Run the report through the redactor and confirm it is secret-free.");
            }

            return DiagnosticResult
                .Pass(ModuleId, "M8.SECRET_FREE_REPORT", correlation, now)
                .WithFindingId("M8.SECRET_FREE_REPORT")
                .WithReasonCode("SECRET_FREE_CONFIRMED")
                .WithObservation("No account identifier, credential or operator path is present in the report.")
                .WithSource(FindingFactory.SourceRuntimeCounters)
                .WithMethod("Every report string passes through SecretRedactor; identities use SyntheticRef hashes.")
                .WithLimitation("Redaction is pattern-based; it is a defence, not a formal proof.")
                .WithNextAction("None.")
                .WithEvidence(DiagnosticEvidence.Observable(FindingFactory.SourceRuntimeCounters, "reportSecretFree=true", now));
        }

        private DiagnosticResult BuildEvidenceIntegrityCheck(ProbeContext context, string correlation, DateTime now)
        {
            var session = context.Session;
            var dropped = session.DroppedCount;
            var gaps = session.SequenceGapCount;
            var invalidTs = session.TimestampInvalidCount;
            var clean = dropped == 0 && gaps == 0 && invalidTs == 0;

            // A positive counter is itself evidence of a problem and fails regardless. Zero
            // counters only mean "clean" once the session actually ingested records; a session
            // that captured nothing has nothing to have been continuous, so the check stays
            // INCONCLUSIVE rather than reporting a clean PASS on absent data.
            if (clean && session.EnqueuedCount == 0)
            {
                return FindingFactory.Unobservable(ModuleId, "M8.EVIDENCE_INTEGRITY", correlation, now,
                    "No event was ingested during this session, so evidence integrity could not be evaluated.",
                    "Run the pass while the host is delivering events.");
            }

            return (clean
                    ? DiagnosticResult.Pass(ModuleId, "M8.EVIDENCE_INTEGRITY", correlation, now)
                    : DiagnosticResult.Fail(ModuleId, "M8.EVIDENCE_INTEGRITY", ErrorClass.SequenceGap, correlation, now))
                .WithFindingId("M8.EVIDENCE_INTEGRITY")
                .WithReasonCode("DROPPED=" + dropped, "SEQUENCE_GAPS=" + gaps, "TIMESTAMP_INVALID=" + invalidTs)
                .WithObservation("dropped=" + dropped + " sequenceGaps=" + gaps + " timestampInvalid=" + invalidTs)
                .WithSource(FindingFactory.SourceRuntimeCounters)
                .WithMethod("Session counters for dropped events, sequence gaps and implausible timestamps.")
                .WithLimitation("A non-zero counter means the evidence is incomplete for this session; the report must not be read as clean.")
                .WithNextAction(clean
                    ? "None."
                    : "Treat the session as incomplete; investigate the drop/gap/timestamp cause before accepting the evidence.")
                .WithEvidence(
                    DiagnosticEvidence.Observable(FindingFactory.SourceRuntimeCounters, "dropped=" + dropped, now),
                    DiagnosticEvidence.Observable(FindingFactory.SourceRuntimeCounters, "sequenceGaps=" + gaps, now),
                    DiagnosticEvidence.Observable(FindingFactory.SourceRuntimeCounters, "timestampInvalid=" + invalidTs, now));
        }

        private DiagnosticResult BuildUnobservableCheck(SafetyObservation observation, string correlation, DateTime now)
        {
            var items = observation.UnobservableItems ?? new List<string>();
            var evidence = new List<DiagnosticEvidence>();
            foreach (var item in items)
            {
                evidence.Add(DiagnosticEvidence.Unobservable(FindingFactory.SourceUnobservable, item, now));
            }

            // Well-known unobservable items are always listed, even when none were supplied.
            evidence.Add(DiagnosticEvidence.Unobservable(FindingFactory.SourceUnobservable, "NINJATRADER_RUNTIME_VERSION", now));
            evidence.Add(DiagnosticEvidence.Unobservable(FindingFactory.SourceUnobservable, "PROVIDER_BUILD", now));
            evidence.Add(DiagnosticEvidence.Unobservable(FindingFactory.SourceUnobservable, "TRADE_AGGRESSOR_SIDE", now));

            return DiagnosticResult
                .NotApplicable(ModuleId, "M8.UNOBSERVABLE", correlation, now)
                .WithFindingId("M8.UNOBSERVABLE")
                .WithReasonCode("UNOBSERVABLE_ITEMS=" + items.Count)
                .WithObservation("Items that cannot be proven through public APIs are listed explicitly and never reported as PASS.")
                .WithSource(FindingFactory.SourceUnobservable)
                .WithMethod("Enumerated the values no documented public API exposes.")
                .WithLimitation("These items require an out-of-band confirmation; they can never be PASS from inside the process.")
                .WithNextAction("Confirm these items out of band and record them in the acceptance report.")
                .WithEvidence(evidence.ToArray());
        }
    }
}
