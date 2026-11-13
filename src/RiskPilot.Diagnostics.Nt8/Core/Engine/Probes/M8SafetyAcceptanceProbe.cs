using System;
using System.Collections.Generic;
using RiskPilot.Diagnostics.Nt8.Core.Models;
using RiskPilot.Diagnostics.Nt8.Core.Models.Observations;
using RiskPilot.Diagnostics.Nt8.Core.Result;
using RiskPilot.Diagnostics.Nt8.Core.Security;
using RiskPilot.Diagnostics.Nt8.Core.Status;

namespace RiskPilot.Diagnostics.Nt8.Core.Engine.Probes
{
    /// <summary>
    /// M8 — Safety &amp; acceptance. It owns and executes the read-only security scan and reports
    /// the tool's runtime safety posture (trading-disabled, read-only), the secret-free state of
    /// the report, the session evidence integrity and the items that remain unobservable.
    /// <para>
    /// The security scan is the single owner of the forbidden-API/identity-confinement checks: it
    /// runs here, inside M8, over the read-only source the data provider supplies, and no other
    /// module executes it, so there is no duplicate scan. It preserves every check the offline
    /// <c>verification/addon-smoke</c> scan performed. Missing runtime evidence, an unavailable
    /// scan source, or an unreadable file is INCONCLUSIVE, never PASS (fail-closed).
    /// </para>
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

            // The security scan is owned by M8 and runs here, independent of whether the host
            // supplied a safety observation: an absent observation must not suppress the scan.
            var scan = RunSecurityScan(context);

            // Publish the single scan result on the session so the evidence export reports the
            // same scan the live probe produced, without a second (duplicate) execution.
            try
            {
                context.Session.SecurityScan = scan;
            }
            catch (Exception)
            {
                // A session that cannot hold the scan still reports it on the live path.
            }

            results.Add(BuildSecurityScanCheck(scan, correlation, now));

            if (observation == null)
            {
                results.Add(FindingFactory.Unobservable(ModuleId, "M8.TRADING_DISABLED", correlation, now,
                    "No safety observation was supplied by the host layer.",
                    "Run the pass inside the host so the runtime safety evidence is captured."));
                results.Add(FindingFactory.Unobservable(ModuleId, "M8.READ_ONLY_POSTURE", correlation, now,
                    "No safety observation was supplied by the host layer.",
                    "Run the pass inside the host so the runtime safety evidence is captured."));
                results.Add(FindingFactory.Unobservable(ModuleId, "M8.SECRET_FREE_REPORT", correlation, now,
                    "No safety observation was supplied by the host layer.",
                    "Run the pass inside the host so the runtime safety evidence is captured."));
                results.Add(BuildEvidenceIntegrityCheck(context, correlation, now));
                results.Add(BuildUnobservableCheck(null, correlation, now));
                return results;
            }

            results.Add(BuildTradingDisabledCheck(observation, correlation, now));
            results.Add(BuildReadOnlyCheck(observation, correlation, now));
            results.Add(BuildSecretFreeCheck(observation, correlation, now));
            results.Add(BuildEvidenceIntegrityCheck(context, correlation, now));
            results.Add(BuildUnobservableCheck(observation, correlation, now));

            return results;
        }

        /// <summary>
        /// Runs the read-only security scan against the provider-supplied source. It never throws:
        /// a null source, an exception or an unreadable source yields an unexecuted observation so
        /// the check stays INCONCLUSIVE rather than PASS.
        /// </summary>
        private static SecurityScanObservation RunSecurityScan(ProbeContext context)
        {
            try
            {
                var source = context.DataProvider.GetSecurityScanSource();
                return SecurityScanner.Scan(source);
            }
            catch (Exception)
            {
                return new SecurityScanObservation
                {
                    Executed = false,
                    UnavailableReason = SecurityScanReasonCodes.Exception,
                    ReasonCodes = new List<string> { SecurityScanReasonCodes.Exception }
                };
            }
        }

        /// <summary>
        /// The read-only security scan result. PASS only when the scan actually executed, found no
        /// forbidden pattern, the AddOn declared the trading-disabled invariant and the distinct
        /// Owner identity literals stay confined to the reviewed definition file. A positive hit is
        /// FAIL (it is itself evidence). An unexecuted/unavailable scan is INCONCLUSIVE.
        /// </summary>
        private DiagnosticResult BuildSecurityScanCheck(SecurityScanObservation scan, string correlation, DateTime now)
        {
            if (scan == null || !scan.Executed)
            {
                var reason = scan != null && !string.IsNullOrEmpty(scan.UnavailableReason)
                    ? scan.UnavailableReason
                    : SecurityScanReasonCodes.SourceUnavailable;

                return DiagnosticResult
                    .Inconclusive(ModuleId, "M8.SECURITY_SCAN", correlation, now)
                    .WithFindingId("M8.SECURITY_SCAN")
                    .WithReasonCode(reason)
                    .WithObservation("The read-only security scan could not run: " + reason + ".")
                    .WithSource(FindingFactory.SourceStaticScan)
                    .WithMethod("Read-only scan of production source for forbidden order/account/reflection/unsafe patterns.")
                    .WithLimitation("Without a readable source the absence of forbidden APIs is unproven; it is never reported as PASS.")
                    .WithNextAction("Run the diagnostic where the production source is readable (the repo tree or the NinjaScript custom-compile folder).")
                    .WithEvidence(DiagnosticEvidence.Unobservable(FindingFactory.SourceStaticScan, reason, now));
            }

            // A positive forbidden-pattern hit, a leaked identity literal, a missing
            // trading-disabled declaration or a definition file that no longer defines the
            // identity literals is itself evidence of a defect: FAIL, never PASS.
            if (scan.HitCount > 0
                || Contains(scan.ReasonCodes, SecurityScanReasonCodes.TradingDisabledMissing)
                || Contains(scan.ReasonCodes, SecurityScanReasonCodes.DefinitionMissing)
                || Contains(scan.ReasonCodes, SecurityScanReasonCodes.DefinitionLiteralMissing))
            {
                var evidence = new List<DiagnosticEvidence>
                {
                    DiagnosticEvidence.Observable(FindingFactory.SourceStaticScan, "forbiddenApiHits=" + scan.HitCount, now),
                    DiagnosticEvidence.Observable(FindingFactory.SourceStaticScan, "filesScanned=" + scan.FilesScanned, now),
                    DiagnosticEvidence.Observable(FindingFactory.SourceStaticScan, "tradingDisabledDeclared=" + scan.TradingDisabledDeclared, now),
                    DiagnosticEvidence.Observable(FindingFactory.SourceStaticScan, "identityDefinitionReviewed=" + scan.IdentityDefinitionReviewed, now)
                };

                // Report each hit by rule id and file only — never a code fragment, so a hit can
                // never leak source into the finding or artifact.
                foreach (var hit in scan.Hits)
                {
                    evidence.Add(DiagnosticEvidence.Observable(FindingFactory.SourceStaticScan,
                        "hit rule=" + hit.RuleId + " category=" + hit.Category + " file=" + hit.File + " line=" + hit.Line, now));
                }

                return DiagnosticResult
                    .Fail(ModuleId, "M8.SECURITY_SCAN", ErrorClass.ConfigurationInvalid, correlation, now)
                    .WithFindingId("M8.SECURITY_SCAN")
                    .WithReasonCode(ToArray(scan.ReasonCodes))
                    .WithObservation("forbiddenApiHits=" + scan.HitCount + " filesScanned=" + scan.FilesScanned
                        + " tradingDisabledDeclared=" + scan.TradingDisabledDeclared
                        + " identityDefinitionReviewed=" + scan.IdentityDefinitionReviewed)
                    .WithSource(FindingFactory.SourceStaticScan)
                    .WithMethod("Read-only scan of production source for forbidden order/account/reflection/unsafe patterns and identity-literal confinement.")
                    .WithLimitation("A static scan proves the absence of forbidden patterns in source; it is not a runtime guarantee.")
                    .WithNextAction("Remove the forbidden pattern (or confirm and fix the identity/invariant finding), then re-run the pass.")
                    .WithEvidence(evidence.ToArray());
            }

            // The scan ran but the AddOn's trading-disabled declaration was not observable (its
            // file was not in the scanned set). The absence of that evidence is not a PASS.
            if (!scan.TradingDisabledObservable)
            {
                return DiagnosticResult
                    .Inconclusive(ModuleId, "M8.SECURITY_SCAN", correlation, now)
                    .WithFindingId("M8.SECURITY_SCAN")
                    .WithReasonCode(SecurityScanReasonCodes.TradingDisabledMissing)
                    .WithObservation("forbiddenApiHits=" + scan.HitCount + " filesScanned=" + scan.FilesScanned
                        + " tradingDisabledDeclared=UNOBSERVABLE")
                    .WithSource(FindingFactory.SourceStaticScan)
                    .WithMethod("Read-only scan of production source; the AddOn declaration file was not in the scanned set.")
                    .WithLimitation("The trading-disabled invariant could not be confirmed because its declaring file was not scanned.")
                    .WithNextAction("Confirm the AddOn source is present in the scanned source set, then re-run.")
                    .WithEvidence(
                        DiagnosticEvidence.Observable(FindingFactory.SourceStaticScan, "forbiddenApiHits=" + scan.HitCount, now),
                        DiagnosticEvidence.Unobservable(FindingFactory.SourceStaticScan, "TRADING_DISABLED_DECLARATION_NOT_OBSERVED", now));
            }

            // The scan ran over a readable set, but it could not cover the whole permitted source
            // set (a skipped file, a reparse point, or an unreadable entry). A clean result over a
            // partially covered set is not a PASS; it is INCONCLUSIVE.
            if (!scan.SourceComplete)
            {
                return DiagnosticResult
                    .Inconclusive(ModuleId, "M8.SECURITY_SCAN", correlation, now)
                    .WithFindingId("M8.SECURITY_SCAN")
                    .WithReasonCode(SecurityScanReasonCodes.Incomplete)
                    .WithObservation("forbiddenApiHits=" + scan.HitCount + " filesScanned=" + scan.FilesScanned
                        + " sourceComplete=false")
                    .WithSource(FindingFactory.SourceStaticScan)
                    .WithMethod("Read-only scan of production source; the permitted source set could not be fully covered.")
                    .WithLimitation("A partially covered source set cannot prove the absence of forbidden patterns; it is never reported as PASS.")
                    .WithNextAction("Confirm every production file under the BOOT directory is readable, then re-run.")
                    .WithEvidence(
                        DiagnosticEvidence.Observable(FindingFactory.SourceStaticScan, "forbiddenApiHits=" + scan.HitCount, now),
                        DiagnosticEvidence.Unobservable(FindingFactory.SourceStaticScan, "SOURCE_SET_NOT_FULLY_COVERED", now));
            }

            return DiagnosticResult
                .Pass(ModuleId, "M8.SECURITY_SCAN", correlation, now)
                .WithFindingId("M8.SECURITY_SCAN")
                .WithReasonCode(SecurityScanReasonCodes.Passed,
                    "FORBIDDEN_API_HITS=" + scan.HitCount,
                    "FILES_SCANNED=" + scan.FilesScanned,
                    "RULES_APPLIED=" + scan.RuleCount)
                .WithObservation("forbiddenApiHits=" + scan.HitCount + " filesScanned=" + scan.FilesScanned
                    + " rules=" + scan.RuleCount + " tradingDisabledDeclared=" + scan.TradingDisabledDeclared)
                .WithSource(FindingFactory.SourceStaticScan)
                .WithMethod("Read-only scan of production source for forbidden order/account/reflection/unsafe patterns and identity-literal confinement.")
                .WithLimitation("A static scan proves the absence of forbidden patterns in source; it is not a runtime guarantee and it never mutates any host configuration.")
                .WithNextAction("None.")
                .WithEvidence(
                    DiagnosticEvidence.Observable(FindingFactory.SourceStaticScan, "forbiddenApiHits=" + scan.HitCount, now),
                    DiagnosticEvidence.Observable(FindingFactory.SourceStaticScan, "filesScanned=" + scan.FilesScanned, now),
                    DiagnosticEvidence.Observable(FindingFactory.SourceStaticScan, "rulesApplied=" + scan.RuleCount, now),
                    DiagnosticEvidence.Observable(FindingFactory.SourceStaticScan, "tradingDisabledDeclared=" + scan.TradingDisabledDeclared, now),
                    DiagnosticEvidence.Observable(FindingFactory.SourceStaticScan, "identityDefinitionReviewed=" + scan.IdentityDefinitionReviewed, now));
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
                    .WithNextAction("Run the pass inside the host; the security scan complements this check.");
            }

            return DiagnosticResult
                .Pass(ModuleId, "M8.READ_ONLY_POSTURE", correlation, now)
                .WithFindingId("M8.READ_ONLY_POSTURE")
                .WithReasonCode("READ_ONLY_POSTURE_OBSERVED")
                .WithObservation("No order/account/connection mutation path is reachable from the tool.")
                .WithSource(FindingFactory.SourcePublicApi)
                .WithMethod("Runtime check plus the security scan of production source.")
                .WithLimitation("Runtime observation is complemented by, but not replaced by, the security scan.")
                .WithNextAction("None.")
                .WithEvidence(DiagnosticEvidence.Observable(FindingFactory.SourcePublicApi, "readOnlyPostureObserved=true", now));
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
            var items = observation != null && observation.UnobservableItems != null
                ? observation.UnobservableItems
                : new List<string>();
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

        private static bool Contains(IList<string> codes, string code)
        {
            if (codes == null)
            {
                return false;
            }

            foreach (var value in codes)
            {
                if (string.Equals(value, code, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static string[] ToArray(IList<string> values)
        {
            if (values == null || values.Count == 0)
            {
                return new string[0];
            }

            var array = new string[values.Count];
            for (var i = 0; i < values.Count; i++)
            {
                array[i] = values[i];
            }

            return array;
        }
    }
}
