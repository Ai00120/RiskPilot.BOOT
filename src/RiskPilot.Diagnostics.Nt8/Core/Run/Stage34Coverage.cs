using System;
using System.Collections.Generic;
using RiskPilot.Diagnostics.Nt8.Core.Result;
using RiskPilot.Diagnostics.Nt8.Core.Status;

namespace RiskPilot.Diagnostics.Nt8.Core.Run
{
    /// <summary>
    /// Per-run coverage status of a Stage 3 / Stage 4 requirement. It is <b>derived</b> from the
    /// checks the run actually produced, never asserted: a requirement is only
    /// <see cref="Verified"/> when every backing check ran and PASSed in this run, and is
    /// <see cref="Inconclusive"/> when the required host evidence was not available (fail-closed —
    /// an unavailable requirement is never reported as PASS).
    /// </summary>
    public static class Stage34CoverageStatus
    {
        /// <summary>Every backing check ran and PASSed in this run.</summary>
        public const string Verified = "VERIFIED";

        /// <summary>A backing check FAILed in this run.</summary>
        public const string Failed = "FAILED";

        /// <summary>The backing checks ran but the required host evidence was not available.</summary>
        public const string Inconclusive = "INCONCLUSIVE";

        /// <summary>The backing module did not run in this mode, so nothing was observed.</summary>
        public const string NotExecuted = "NOT_EXECUTED";

        /// <summary>The requirement has no runtime host API; it is only verifiable at source level.</summary>
        public const string SourceOnly = "SOURCE_ONLY";

        /// <summary>No BOOT module provides evidence for the requirement.</summary>
        public const string NotCovered = "NOT_COVERED";
    }

    /// <summary>
    /// How completely BOOT can verify a requirement, independent of any single run. This is the
    /// static boundary that stops a run from claiming more than the host surface can support.
    /// </summary>
    public static class Stage34VerificationScope
    {
        /// <summary>The requirement is fully observable from the public host surface.</summary>
        public const string Full = "FULL";

        /// <summary>BOOT can observe only part of the requirement (for example reason-code outcomes).</summary>
        public const string Partial = "PARTIAL";

        /// <summary>BOOT cannot verify the requirement at runtime at all.</summary>
        public const string None = "NONE";
    }

    /// <summary>
    /// Where the evidence for a requirement can come from. It separates a genuine runtime
    /// observation from a source-level fact, so a reader never mistakes one for the other.
    /// </summary>
    public static class Stage34EvidenceClass
    {
        /// <summary>Observable from the live NinjaTrader host public APIs.</summary>
        public const string RuntimeHost = "RUNTIME_HOST";

        /// <summary>Verifiable only by reading production source, not from the running host.</summary>
        public const string SourceLevel = "SOURCE_LEVEL";

        /// <summary>Neither a host observation nor a source fact; for example RiskPilot.BOT-internal provenance.</summary>
        public const string NotVerifiable = "NOT_VERIFIABLE";
    }

    /// <summary>
    /// One Stage 3 / Stage 4 requirement traced to the BOOT module(s) and check(s) that can
    /// provide evidence for it, together with the host data required, the verification method,
    /// the static scope/evidence class and the documented current limitation.
    /// </summary>
    public sealed class Stage34Requirement
    {
        public string Id { get; set; }

        /// <summary>"Stage 3" or "Stage 4" (or "Identity"/"Data" for an upstream dependency).</summary>
        public string Stage { get; set; }

        public string Title { get; set; }

        /// <summary>The requirement text as defined by RiskPilot.BOT.</summary>
        public string Requirement { get; set; }

        /// <summary>The RiskPilot.BOT owner decision / document that defines it.</summary>
        public string BotReference { get; set; }

        /// <summary>BOOT module ids that provide evidence (empty when none do).</summary>
        public IList<string> BootModules { get; set; }

        /// <summary>Exact BOOT check ids that provide evidence (empty when none do).</summary>
        public IList<string> BootChecks { get; set; }

        /// <summary>The host data the evidence depends on.</summary>
        public string HostData { get; set; }

        /// <summary>How the evidence is collected.</summary>
        public string VerificationMethod { get; set; }

        /// <summary><see cref="Stage34EvidenceClass"/> value.</summary>
        public string EvidenceClass { get; set; }

        /// <summary><see cref="Stage34VerificationScope"/> value.</summary>
        public string VerificationScope { get; set; }

        /// <summary>True when the check requires a real NinjaTrader installation to observe.</summary>
        public bool RequiresRealHost { get; set; }

        /// <summary>True when the check requires an actual connected Rithmic simulation account.</summary>
        public bool RequiresRithmicSimAccount { get; set; }

        /// <summary>The current limitation of BOOT's coverage of the requirement.</summary>
        public string CurrentLimitation { get; set; }

        public Stage34Requirement()
        {
            Id = string.Empty;
            Stage = string.Empty;
            Title = string.Empty;
            Requirement = string.Empty;
            BotReference = string.Empty;
            BootModules = new List<string>();
            BootChecks = new List<string>();
            HostData = string.Empty;
            VerificationMethod = string.Empty;
            EvidenceClass = Stage34EvidenceClass.RuntimeHost;
            VerificationScope = Stage34VerificationScope.Partial;
            CurrentLimitation = string.Empty;
        }
    }

    /// <summary>
    /// The per-run result for one requirement: the static definition plus the coverage status
    /// derived from the checks this run actually produced.
    /// </summary>
    public sealed class Stage34RequirementCoverage
    {
        public Stage34Requirement Requirement { get; set; }

        /// <summary><see cref="Stage34CoverageStatus"/> value derived from this run.</summary>
        public string Status { get; set; }

        /// <summary>Number of backing checks present in the run.</summary>
        public int ObservedChecks { get; set; }

        /// <summary>Number of backing checks that PASSed.</summary>
        public int VerifiedChecks { get; set; }

        /// <summary>Number of backing checks that FAILed.</summary>
        public int FailedChecks { get; set; }

        /// <summary>Number of backing checks that were INCONCLUSIVE.</summary>
        public int InconclusiveChecks { get; set; }

        /// <summary>The check ids this run observed for the requirement.</summary>
        public IList<string> ObservedCheckIds { get; set; }

        public Stage34RequirementCoverage()
        {
            Status = Stage34CoverageStatus.NotExecuted;
            ObservedCheckIds = new List<string>();
        }
    }

    /// <summary>
    /// The full Stage 3 / Stage 4 coverage view for one run: every traced requirement with its
    /// derived status, plus the headline counts and an explicit, honest statement of what BOOT
    /// can and cannot validate.
    /// </summary>
    public sealed class Stage34CoverageReport
    {
        public IList<Stage34RequirementCoverage> Requirements { get; set; }

        public int TotalRequirements { get; set; }

        public int VerifiedCount { get; set; }

        public int FailedCount { get; set; }

        public int InconclusiveCount { get; set; }

        public int NotExecutedCount { get; set; }

        public int SourceOnlyCount { get; set; }

        public int NotCoveredCount { get; set; }

        /// <summary>Requirements that are fully observable from the host surface when data is present.</summary>
        public int FullyVerifiableCount { get; set; }

        /// <summary>Requirements BOOT can only partially observe.</summary>
        public int PartiallyVerifiableCount { get; set; }

        /// <summary>Requirements BOOT cannot verify at runtime.</summary>
        public int NotRuntimeVerifiableCount { get; set; }

        /// <summary>Requirements whose verification needs a real NinjaTrader installation.</summary>
        public int RequiresRealHostCount { get; set; }

        /// <summary>Requirements whose verification needs an actual connected Rithmic SIM account.</summary>
        public int RequiresRithmicSimAccountCount { get; set; }

        /// <summary>
        /// An explicit statement that BOOT does not fully validate Stage 3 or Stage 4. It is
        /// always emitted so no reader can mistake this coverage view for a full validation.
        /// </summary>
        public string Statement { get; set; }

        public Stage34CoverageReport()
        {
            Requirements = new List<Stage34RequirementCoverage>();
            Statement = string.Empty;
        }
    }

    /// <summary>
    /// The single source of truth for the Stage 3 / Stage 4 traceability matrix. Each requirement
    /// records the RiskPilot.BOT owner decision that defines it, the BOOT module(s) and exact
    /// check id(s) that provide evidence, the host data required, the verification method, the
    /// static evidence class / scope and the current limitation.
    /// <para>
    /// The matrix is deliberately conservative: a requirement with no runtime host API is marked
    /// <see cref="Stage34EvidenceClass.SourceLevel"/> and
    /// <see cref="Stage34VerificationScope.None"/>, and one BOOT cannot observe at all is
    /// <see cref="Stage34EvidenceClass.NotVerifiable"/>. This is how the report separates
    /// source-level verification from actual runtime verification.
    /// </para>
    /// </summary>
    public static class Stage34CoverageCatalog
    {
        public static IReadOnlyList<Stage34Requirement> All()
        {
            return new[]
            {
                Req("S3-D022-DUAL-POLICY", "Stage 3", "Dual-policy sizing architecture",
                    "PERCENTAGE_OF_RISK is the default policy; FIXED_QUANTITY is enabled only by explicit configuration; automatic fallback between policies is prohibited.",
                    "D-022",
                    new[] { "M7" }, new[] { "M7.STAGE3_SIZING", "M7.REASON_ATTRIBUTION" },
                    "RiskPilot.BOT read-only sizing reason-code distribution (IReadOnlyDiagnosticSource.ReadStrategies / sizing diagnostics).",
                    "Read the reported Stage 3 reason-code distribution and attribute each code to the sizing policy outcome.",
                    Stage34EvidenceClass.RuntimeHost, Stage34VerificationScope.Partial, false, false,
                    "BOOT can observe the sizing reason codes only when RiskPilot.BOT exposes its read-only diagnostics; the policy-selection logic itself is BOT-internal and is not re-derived here."),

                Req("S3-D022-FAILCLOSED", "Stage 3", "Fail-closed sizing on missing inputs",
                    "Missing, invalid, stale or unverified required inputs fail closed with RequestedQuantity = 0 and an explicit reason code (never a fabricated or defaulted quantity).",
                    "D-022",
                    new[] { "M7" }, new[] { "M7.STAGE3_SIZING" },
                    "Stage 3 sizing reason-code distribution including zero-quantity outcomes.",
                    "Count the Stage 3 outcomes and the zero-quantity outcomes; a zero count is only meaningful once the sizing surface was observed.",
                    Stage34EvidenceClass.RuntimeHost, Stage34VerificationScope.Partial, true, false,
                    "A zero-quantity outcome is reported only as a raw distribution; BOOT does not re-evaluate the sizing math, so it cannot prove a specific input caused a specific reason."),

                Req("S3-D022-QUANTITY-LATTICE", "Stage 3", "Quantity rounding / min / cap rules",
                    "Quantity is rounded DOWN to a multiple of QuantityStep; a result below QuantityMin fails closed rather than inflating; QuantityMax is enforced.",
                    "D-022",
                    new[] { "M7" }, new[] { "M7.STAGE3_SIZING" },
                    "Stage 3 sizing reason codes (for example CALCULATED_QUANTITY_BELOW_MINIMUM / CAPPED_AT_QUANTITY_MAX).",
                    "Read the Stage 3 reason-code distribution and report the lattice-related outcomes.",
                    Stage34EvidenceClass.RuntimeHost, Stage34VerificationScope.Partial, true, false,
                    "BOOT reports the lattice reason codes as a distribution; it does not independently recompute the quantity lattice."),

                Req("S3-ANALYTICAL-NONEXECUTABLE", "Stage 3", "Stage 3 output is analytical and non-executable",
                    "Stage 3 produces an analytical SizedTradeProposal only; it never submits, modifies or cancels an order, and Trading=Disabled is preserved.",
                    "D-022 / cross-stage invariant 1-2",
                    new[] { "M8" }, new[] { "M8.SECURITY_SCAN", "M8.READ_ONLY_POSTURE", "M8.TRADING_DISABLED" },
                    "Read-only scan of production source plus the tool's runtime read-only posture.",
                    "Static safety scan of production source for forbidden order/execution APIs, plus the runtime trading-disabled / read-only posture.",
                    Stage34EvidenceClass.RuntimeHost, Stage34VerificationScope.Full, true, false,
                    "The scan proves the absence of forbidden patterns in the scanned source; it is a source-level guarantee, not a proof about RiskPilot.BOT's own source."),

                Req("S3-OD1-INVALIDATION", "Stage 3", "Structured InvalidationLevel provenance (OD-1)",
                    "PERCENTAGE_OF_RISK requires a positive numerical InvalidationLevel; prose parsing and fabricated levels are prohibited; a null level fails closed.",
                    "OD-1 / D-074 / D-075",
                    new string[0], new string[0],
                    "None: no public NinjaTrader API exposes RiskPilot.BOT's per-candidate InvalidationLevel provenance.",
                    "Not observable from the host; the boundary is documented from the RiskPilot.BOT contract.",
                    Stage34EvidenceClass.SourceLevel, Stage34VerificationScope.None, false, false,
                    "No public host API exposes the invalidation producer; BOOT cannot verify it at runtime and reports it as SOURCE_ONLY."),

                Req("S4-MONOTONIC-QUANTITY", "Stage 4", "Monotonic quantity invariant",
                    "0 <= ApprovedQuantity <= RequestedQuantity; a veto means ApprovedQuantity = 0; Stage 4 never originates or increases quantity.",
                    "D-022 / G3-c",
                    new[] { "M7" }, new[] { "M7.RISK_GATE" },
                    "Stage 4 risk-gate reason-code distribution (approvals and vetoes).",
                    "Read the Stage 4 risk-gate reason-code distribution and report approvals versus vetoes.",
                    Stage34EvidenceClass.RuntimeHost, Stage34VerificationScope.Partial, true, false,
                    "BOOT observes the risk-gate outcome distribution, not the internal quantity arithmetic; the invariant itself is BOT-internal."),

                Req("S4-FAILCLOSED", "Stage 4", "Fail-closed risk gate on missing inputs",
                    "A missing/invalid risk policy, account state, instrument specification or portfolio evidence vetoes Stage 4 with ApprovedQuantity = 0 and an explicit reason.",
                    "D-022 / D-032 / G3-c",
                    new[] { "M7" }, new[] { "M7.RISK_GATE", "M7.REASON_ATTRIBUTION" },
                    "Stage 4 veto reason-code distribution.",
                    "Read the Stage 4 veto reason distribution and attribute each code to its cause.",
                    Stage34EvidenceClass.RuntimeHost, Stage34VerificationScope.Partial, true, false,
                    "BOOT reports the veto reasons it observes; it does not reproduce the gate's decision logic."),

                Req("S4-ACCOUNT-STATE-GATE", "Stage 4", "Non-bypassable verified account state (D-032)",
                    "Account-state verification is unconditional; a missing, unverified, stale or causally invalid account snapshot vetoes Stage 4; the state-injection setters are non-public.",
                    "D-032 / OD-3",
                    new[] { "M2", "M7" }, new[] { "M2.ACCOUNT_IDENTITY", "M7.RISK_GATE" },
                    "Account identity metadata (Account.All / Connection) and the Stage 4 veto reason distribution.",
                    "Verify the target account identity/connection, and read the Stage 4 veto reasons for account-state causes.",
                    Stage34EvidenceClass.RuntimeHost, Stage34VerificationScope.Partial, true, true,
                    "BOOT proves the account identity and observes account-state vetoes; it cannot read RiskPilot.BOT's in-memory AccountStateSnapshot or prove the setter visibility at runtime (that is a source fact)."),

                Req("S4-RISK-POLICY", "Stage 4", "Compiled immutable RiskPolicyLimits with verified hash (D-036 / D-073)",
                    "The active SIM risk policy is a compiled, immutable, versioned profile whose content hash is recomputed and compared at resolution; an integrity failure fails closed.",
                    "D-036 / D-073 / OD-6",
                    new string[0], new string[0],
                    "None: the compiled profile and its hash live inside RiskPilot.BOT; no host API exposes them.",
                    "Not observable from the host; the boundary is documented from the RiskPilot.BOT contract.",
                    Stage34EvidenceClass.SourceLevel, Stage34VerificationScope.None, false, false,
                    "The compiled policy profile and its SHA-256 are RiskPilot.BOT source facts; BOOT cannot verify them at runtime and reports this as SOURCE_ONLY."),

                Req("S4-PORTFOLIO-STATE", "Stage 4", "Dynamic portfolio state is not zero-defaulted (OD-2)",
                    "Unknown dynamic portfolio metrics must never silently default to zero; a missing measurement must be required or represented as unavailable and fail closed.",
                    "OD-2 / D-031",
                    new string[0], new string[0],
                    "None: portfolio state is RiskPilot.BOT-internal; no public NinjaTrader API exposes it.",
                    "Not observable from the host; the boundary is documented from the RiskPilot.BOT contract.",
                    Stage34EvidenceClass.SourceLevel, Stage34VerificationScope.None, false, false,
                    "The zero-default hazard is a RiskPilot.BOT source property; BOOT cannot verify it at runtime and reports this as SOURCE_ONLY."),

                Req("S34-CORRELATION", "Stage 4", "Decision→proposal→risk linkage preserved",
                    "The Stage 2 decision id, Stage 3 proposal id and Stage 4 risk-evaluation id are preserved and correlated end to end.",
                    "D-022 invariant 7 / G3-b / G3-c",
                    new[] { "M7" }, new[] { "M7.STAGE_CORRELATION" },
                    "The three correlation ids supplied by the read-only diagnostic source.",
                    "Correlate the decision id, proposal id and risk-evaluation id; PASS only when all three are present and consistent.",
                    Stage34EvidenceClass.RuntimeHost, Stage34VerificationScope.Full, true, false,
                    "The correlation is only PASS when the read-only source actually supplies the three ids; without them it stays INCONCLUSIVE."),

                Req("ID-ACCOUNT", "Identity", "Exact account identity (D-095 / D-096)",
                    "The operational account is exactly the permanent single-account identity: the exact account name, provider Rithmic, on the existing bound connection, Simulation mode, USD. (The literal account/connection names live only in the reviewed identity definition and are never repeated here.)",
                    "D-095 / D-096",
                    new[] { "M2" }, new[] { "M2.ACCOUNT_IDENTITY", "M2.ACCOUNT_CONNECTION_ASSOCIATION" },
                    "Account.All / Account.Provider / Account.Connection / ConnectOptions / Account.Denomination.",
                    "Enumerate Account.All, match the exact account name ordinally, and compare provider / connection / mode / denomination (fail-closed on any unobservable or mismatching field).",
                    Stage34EvidenceClass.RuntimeHost, Stage34VerificationScope.Full, true, true,
                    "Requires the real Rithmic SIM account to be connected; with no account surface the check is INCONCLUSIVE, never PASS."),

                Req("ID-CONNECTION-PROVIDER", "Identity", "Exact connection / provider identity (D-096)",
                    "The account is bound to exactly the permanent connection identity with provider Rithmic, connected, and it is the account's own bound connection (never the first of several).",
                    "D-096",
                    new[] { "M2" }, new[] { "M2.PROVIDER_IDENTITY", "M2.ACCOUNT_CONNECTION_ASSOCIATION", "M2.CONNECTIONS" },
                    "Connection.Connections / ConnectOptions.Provider / the account's own Connection.",
                    "Resolve the provider by string comparison on the target account's own connection (never the first of several), and confirm both feed states.",
                    Stage34EvidenceClass.RuntimeHost, Stage34VerificationScope.Full, true, true,
                    "Provider identity is resolved by string comparison (no guaranteed Rithmic enum member); the provider build is unobservable and never reported as PASS."),

                Req("ID-INSTRUMENT", "Identity", "Canonical ES/CME instrument identity (D-078 / D-096)",
                    "The loaded instrument is the ES root on the CME venue, type Future, currency USD; the venue is confirmed by the D-096 configured route, never inferred from the symbol text.",
                    "D-078 / D-096 / D-095 §4",
                    new[] { "M4" }, new[] { "M4.INSTRUMENT_IDENTITY", "M4.D096_ROUTE_CANDIDATE", "M4.REGISTRY_MATCH" },
                    "Instrument.All / MasterInstrument.Name / InstrumentType / Currency / Instrument.Exchange / MasterInstrument.Exchanges.",
                    "Read the loaded instrument discovery surface, match the root exactly, and confirm the configured Globex/Rithmic route (fail-closed on unconfirmed exchange metadata).",
                    Stage34EvidenceClass.RuntimeHost, Stage34VerificationScope.Full, true, true,
                    "The venue is a configured-route mapping (Owner-authorized D-096), not an independent exchange-issued listing-venue attestation; the raw symbol is never reported."),

                Req("ID-ES-ECONOMICS", "Identity", "Canonical ES economic specification (D-095 §4)",
                    "The ES product economics are point value 50, tick size 0.25, derived tick value 12.50 and USD.",
                    "D-095 §4 / D-096 §3",
                    new[] { "M4" }, new[] { "M4.ES_PRODUCT_SPEC" },
                    "MasterInstrument.PointValue / TickSize / Currency of the loaded ES instrument.",
                    "Compare the loaded instrument's own economics against the canonical CME ES contract facts (fail-closed on any unobservable or mismatching field).",
                    Stage34EvidenceClass.RuntimeHost, Stage34VerificationScope.Full, true, true,
                    "The canonical economics are published CME contract facts used as reference expectations, not an independent exchange attestation; the raw symbol is never reported."),

                Req("DEP-DOM-MARKETDATA", "Data", "DOM / Level II and Level I market-data surface",
                    "Stage 3/4 sizing and the order-flow inputs depend on a live Level I/Level II market-data surface with observable integrity and freshness.",
                    "D-073 / data contract",
                    new[] { "M5" }, new[] { "M5.DOM_SURFACE", "M5.DEPTH", "M5.LEVEL1", "M5.DATA_INTEGRITY", "M5.FRESHNESS", "M5.VALIDITY" },
                    "Instrument.MarketData (Level I) and Instrument.MarketDepth (Level II) update events.",
                    "Subscribe read-only to the loaded instrument's market data / market depth and derive surface, integrity, freshness and validity evidence.",
                    Stage34EvidenceClass.RuntimeHost, Stage34VerificationScope.Full, true, true,
                    "Freshness is always INCONCLUSIVE and only labelled FRESH/STALE; depth is never inferred from a Level I quote."),

                Req("DEP-TRADEDELTA", "Data", "TradeDelta / OrderFlow inputs",
                    "The order-flow readiness inputs depend on a live trade stream with an observable sequence and direction source.",
                    "PHASE4_ORDER_FLOW_SPEC",
                    new[] { "M6" }, new[] { "M6.TRADE_STREAM", "M6.SEQUENCE_CONTINUITY", "M6.DIRECTION_SOURCE", "M6.TRADEDELTA_COMPLETENESS" },
                    "Level I trade prints plus the best bid/ask carried on the same event.",
                    "Ingest trade prints, derive the direction from the BBO (never provider-confirmed), and measure sequence continuity and completeness.",
                    Stage34EvidenceClass.RuntimeHost, Stage34VerificationScope.Partial, true, true,
                    "NT8's public Level I surface exposes no aggressor side, so direction is always derived; sequence continuity stays INCONCLUSIVE without an observed source sequence."),

                Req("DEP-POSTTRADE-AUDIT", "Data", "Order / execution / position lifecycle audit",
                    "The post-trade audit reconciles already-recorded order, execution and position events with correlation, timing and missing/duplicate detection.",
                    "PHASE3_RECORDING_SPECIFICATION / M9",
                    new[] { "M9" }, new[] { "M9.ORDER_LIFECYCLE", "M9.EXECUTION_LIFECYCLE", "M9.CORRELATION_TIMING", "M9.MISSING_OR_DUPLICATE", "M9.AUDIT_SURFACE" },
                    "Account.Orders snapshots plus any ingested order/execution/position events.",
                    "Read the already-recorded order/execution/position surface and reconcile lifecycles, correlation/timing and missing/duplicate events.",
                    Stage34EvidenceClass.RuntimeHost, Stage34VerificationScope.Partial, true, true,
                    "The audit only covers events recorded while the tool ran; execution coverage is derived (the host reads only Account.Orders), never assumed.")
            };
        }

        private static Stage34Requirement Req(
            string id, string stage, string title, string requirement, string botReference,
            string[] modules, string[] checks, string hostData, string method,
            string evidenceClass, string scope, bool requiresRealHost, bool requiresRithmicSim,
            string limitation)
        {
            return new Stage34Requirement
            {
                Id = id,
                Stage = stage,
                Title = title,
                Requirement = requirement,
                BotReference = botReference,
                BootModules = new List<string>(modules),
                BootChecks = new List<string>(checks),
                HostData = hostData,
                VerificationMethod = method,
                EvidenceClass = evidenceClass,
                VerificationScope = scope,
                RequiresRealHost = requiresRealHost,
                RequiresRithmicSimAccount = requiresRithmicSim,
                CurrentLimitation = limitation
            };
        }
    }

    /// <summary>
    /// Evaluates the Stage 3 / Stage 4 coverage matrix against a completed run. It reads only the
    /// run's reported results, so the live view and the exported artifact agree, and it never
    /// upgrades an unobserved requirement to PASS: a requirement whose backing checks did not run
    /// is <see cref="Stage34CoverageStatus.NotExecuted"/>, and one whose checks ran but found no
    /// host evidence is <see cref="Stage34CoverageStatus.Inconclusive"/>.
    /// </summary>
    public static class Stage34CoverageEvaluator
    {
        public static Stage34CoverageReport Evaluate(DiagnosticRunResult result)
        {
            var results = result != null && result.Report != null ? result.Report.Results : null;
            var byCheck = new Dictionary<string, DiagnosticResult>(StringComparer.OrdinalIgnoreCase);
            if (results != null)
            {
                foreach (var r in results)
                {
                    if (r != null && !string.IsNullOrEmpty(r.CheckId))
                    {
                        byCheck[r.CheckId] = r;
                    }
                }
            }

            var report = new Stage34CoverageReport();
            foreach (var requirement in Stage34CoverageCatalog.All())
            {
                var coverage = Evaluate(requirement, byCheck);
                report.Requirements.Add(coverage);

                switch (coverage.Status)
                {
                    case Stage34CoverageStatus.Verified:
                        report.VerifiedCount++;
                        break;
                    case Stage34CoverageStatus.Failed:
                        report.FailedCount++;
                        break;
                    case Stage34CoverageStatus.Inconclusive:
                        report.InconclusiveCount++;
                        break;
                    case Stage34CoverageStatus.NotExecuted:
                        report.NotExecutedCount++;
                        break;
                    case Stage34CoverageStatus.SourceOnly:
                        report.SourceOnlyCount++;
                        break;
                    default:
                        report.NotCoveredCount++;
                        break;
                }

                switch (requirement.VerificationScope)
                {
                    case Stage34VerificationScope.Full:
                        report.FullyVerifiableCount++;
                        break;
                    case Stage34VerificationScope.Partial:
                        report.PartiallyVerifiableCount++;
                        break;
                    default:
                        report.NotRuntimeVerifiableCount++;
                        break;
                }

                if (requirement.RequiresRealHost)
                {
                    report.RequiresRealHostCount++;
                }

                if (requirement.RequiresRithmicSimAccount)
                {
                    report.RequiresRithmicSimAccountCount++;
                }
            }

            report.TotalRequirements = report.Requirements.Count;
            report.Statement = BuildStatement(report);
            return report;
        }

        private static Stage34RequirementCoverage Evaluate(
            Stage34Requirement requirement,
            IDictionary<string, DiagnosticResult> byCheck)
        {
            var coverage = new Stage34RequirementCoverage { Requirement = requirement };

            // A requirement with no runtime host API is reported SOURCE_ONLY, never as a pass.
            if (requirement.EvidenceClass != Stage34EvidenceClass.RuntimeHost || requirement.BootChecks.Count == 0)
            {
                coverage.Status = Stage34CoverageStatus.SourceOnly;
                return coverage;
            }

            var failed = 0;
            var passed = 0;
            var inconclusive = 0;
            foreach (var checkId in requirement.BootChecks)
            {
                if (!byCheck.TryGetValue(checkId, out var result))
                {
                    continue;
                }

                coverage.ObservedChecks++;
                coverage.ObservedCheckIds.Add(checkId);

                switch (result.Status)
                {
                    case DiagnosticStatus.FAIL:
                        failed++;
                        break;
                    case DiagnosticStatus.PASS:
                        passed++;
                        break;
                    case DiagnosticStatus.NOT_APPLICABLE:
                        // Not applicable is not a verification of the requirement.
                        break;
                    default:
                        inconclusive++;
                        break;
                }
            }

            coverage.VerifiedChecks = passed;
            coverage.FailedChecks = failed;
            coverage.InconclusiveChecks = inconclusive;

            if (coverage.ObservedChecks == 0)
            {
                coverage.Status = Stage34CoverageStatus.NotExecuted;
            }
            else if (failed > 0)
            {
                coverage.Status = Stage34CoverageStatus.Failed;
            }
            else if (coverage.ObservedChecks == requirement.BootChecks.Count && passed == requirement.BootChecks.Count)
            {
                // Every backing check ran and PASSed. A requirement with more than one backing
                // check is only VERIFIED when ALL of them were observed, so a partial run can
                // never claim the requirement is verified.
                coverage.Status = Stage34CoverageStatus.Verified;
            }
            else
            {
                // At least one backing check did not positively pass, or a backing check was not
                // present in this run (the module did not run in this mode). Either way the
                // requirement is unproven, never PASS.
                coverage.Status = Stage34CoverageStatus.Inconclusive;
            }

            return coverage;
        }

        private static string BuildStatement(Stage34CoverageReport report)
        {
            // The statement is always emitted and always states the boundary explicitly: BOOT does
            // NOT fully validate Stage 3 or Stage 4, because several requirements are source-level
            // only and several need a real host / a connected Rithmic SIM account. A run can never
            // upgrade this statement to a claim of full validation.
            var head = report.NotRuntimeVerifiableCount > 0
                ? "BOOT does NOT fully validate Stage 3 or Stage 4: "
                : "BOOT does NOT claim full validation of Stage 3 or Stage 4: ";

            return head + "of " + report.TotalRequirements
                + " traced requirement(s): " + report.VerifiedCount + " verified, " + report.FailedCount
                + " failed, " + report.InconclusiveCount + " inconclusive, " + report.NotExecutedCount
                + " not executed, " + report.SourceOnlyCount + " source-level only and " + report.NotCoveredCount
                + " not covered. " + report.FullyVerifiableCount + " are fully runtime-observable, "
                + report.PartiallyVerifiableCount + " partially, and " + report.NotRuntimeVerifiableCount
                + " not verifiable at runtime. " + report.RequiresRealHostCount
                + " need a real NinjaTrader installation and " + report.RequiresRithmicSimAccountCount
                + " need an actual connected Rithmic simulation account.";
        }
    }
}
