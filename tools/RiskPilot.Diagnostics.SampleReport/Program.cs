using System;
using System.Collections.Generic;
using System.IO;
using RiskPilot.Diagnostics.Nt8.Core.Evidence;
using RiskPilot.Diagnostics.Nt8.Core.Identity;
using RiskPilot.Diagnostics.Nt8.Core.Models;
using RiskPilot.Diagnostics.Nt8.Core.Models.Observations;
using RiskPilot.Diagnostics.Nt8.Core.Report;
using RiskPilot.Diagnostics.Nt8.Core.Result;
using RiskPilot.Diagnostics.Nt8.Core.Run;
using RiskPilot.Diagnostics.Nt8.Core.Security;
using RiskPilot.Diagnostics.Nt8.Core.Session;
using RiskPilot.Diagnostics.Nt8.Core.Status;

namespace RiskPilot.Diagnostics.SampleReport
{
    /// <summary>
    /// Builds a SAMPLE evidence package with the real production builder so the package shape
    /// can be reviewed without a NinjaTrader host. All data is synthetic and every artifact
    /// carries a SAMPLE marker; this is documentation, not a runtime diagnostic result.
    /// </summary>
    public static class Program
    {
        public static int Main(string[] args)
        {
            var outputRoot = args != null && args.Length > 0 && !string.IsNullOrEmpty(args[0])
                ? args[0]
                : Path.Combine("docs", "samples", "sample-evidence-package");

            var sessionId = SessionId.New(new DateTime(2026, 11, 12, 8, 0, 0, DateTimeKind.Utc));
            var runResult = BuildRunResult(sessionId);
            var dataset = BuildDataset(sessionId);

            var package = EvidencePackageBuilder.Build(runResult, dataset);
            if (!package.VerifyIntegrity(out var failure))
            {
                Console.Error.WriteLine("SAMPLE package failed its own integrity check: " + failure);
                return 1;
            }

            if (!package.HasCompleteArtifactSet())
            {
                Console.Error.WriteLine("SAMPLE package is missing one or more required artifacts.");
                return 1;
            }

            if (!package.IsSecretFree(out var offender))
            {
                Console.Error.WriteLine("SAMPLE package is not secret-free (offender: " + offender + ").");
                return 1;
            }

            if (!package.ManifestIsIdentitySafe())
            {
                Console.Error.WriteLine("SAMPLE package manifest carries a raw identity reference.");
                return 1;
            }

            Directory.CreateDirectory(outputRoot);
            foreach (var name in EvidencePackageCatalog.All)
            {
                package.Files.TryGetValue(name, out var content);
                File.WriteAllText(Path.Combine(outputRoot, name), content ?? string.Empty);
            }

            File.WriteAllText(Path.Combine(outputRoot, "SAMPLE_README.txt"),
                "SAMPLE ONLY.\r\n"
                + "This directory is a synthetic documentation sample produced by\r\n"
                + "tools/RiskPilot.Diagnostics.SampleReport. It is NOT a runtime diagnostic result\r\n"
                + "and contains no real account, connection, order or market data. The package was\r\n"
                + "built with the real EvidencePackageBuilder so its shape and SHA-256 manifest match\r\n"
                + "a real run. The numeric values here are deliberately different from the historical\r\n"
                + "11 Nov 2026 baseline so this sample can never be mistaken for that recorded run.\r\n");

            Console.WriteLine("SAMPLE evidence package written to: " + Path.GetFullPath(outputRoot));
            Console.WriteLine("Artifacts: " + EvidencePackageCatalog.All.Count);
            Console.WriteLine("Aggregate status: " + package.Manifest.AggregateStatus);
            Console.WriteLine("Manifest SHA-256: " + package.Manifest.ManifestSha256);
            return 0;
        }

        private static DiagnosticRunResult BuildRunResult(string sessionId)
        {
            var correlation = "sample000000";
            var started = new DateTime(2026, 11, 12, 8, 0, 0, DateTimeKind.Utc);
            var finished = new DateTime(2026, 11, 12, 8, 0, 4, DateTimeKind.Utc);

            var report = new DiagnosticReport
            {
                SessionId = sessionId,
                CorrelationId = correlation,
                GeneratedAtUtc = finished,
                SessionStartedAtUtc = started,
                EnvironmentClass = "SIMULATION"
            };

            void Add(string module, string check, DiagnosticStatus status, string observation, ErrorClass errorClass = ErrorClass.None, params string[] codes)
            {
                var result = status == DiagnosticStatus.PASS
                    ? DiagnosticResult.Pass(module, check, correlation, finished)
                    : status == DiagnosticStatus.FAIL
                        ? DiagnosticResult.Fail(module, check, errorClass, correlation, finished)
                        : status == DiagnosticStatus.NOT_APPLICABLE
                            ? DiagnosticResult.NotApplicable(module, check, correlation, finished)
                            : DiagnosticResult.Inconclusive(module, check, correlation, finished);

                result.WithFindingId(check)
                    .WithObservation(observation)
                    .WithSource("NinjaTrader Public API")
                    .WithMethod("SAMPLE data (synthetic).")
                    .WithLimitation("Synthetic documentation sample; not a runtime observation.")
                    .WithNextAction("None (sample).")
                    .WithReasonCode(codes);
                report.Add(result);
                report.AddFinding(new DiagnosticFinding
                {
                    CheckId = check,
                    ModuleId = module,
                    FindingId = check,
                    Status = status,
                    Severity = status == DiagnosticStatus.FAIL ? DiagnosticSeverity.High : DiagnosticSeverity.Info,
                    Observation = observation,
                    Source = "NinjaTrader Public API",
                    Method = "SAMPLE data (synthetic).",
                    Limitation = "Synthetic documentation sample; not a runtime observation.",
                    NextAction = "None (sample).",
                    ReasonCodes = codes,
                    ErrorClass = errorClass,
                    ObservedAtUtc = finished
                });
            }

            Add("M1", "M1.API_SURFACE", DiagnosticStatus.PASS, "present=7 missing=0", ErrorClass.None, "ALL_REQUIRED_APIS_PRESENT");
            Add("M1", "M1.HOST_VERSION", DiagnosticStatus.INCONCLUSIVE, "NOT OBSERVED: no public API", ErrorClass.None, "HOST_VERSION_UNOBSERVABLE");
            Add("M1", "M1.LIFECYCLE", DiagnosticStatus.PASS, "states=SetDefaults -> Configure -> Active -> DataLoaded -> Realtime transitions=5 current=Realtime", ErrorClass.None, "LIFECYCLE_VALID");
            Add("M1", "M1.LIFECYCLE_PHASES", DiagnosticStatus.PASS, "phases=INITIALIZATION -> STARTUP -> RUNTIME connection=DEFERRED_TO_M2", ErrorClass.None, "PHASES_OBSERVED=3", "CONNECTION=DEFERRED_TO_M2");
            Add("M2", "M2.CONNECTION.BROKER", DiagnosticStatus.PASS, "price=CONNECTED order=CONNECTED", ErrorClass.None, "FEEDS_CONNECTED");
            Add("M2", "M2.PROVIDER_IDENTITY", DiagnosticStatus.PASS, "provider=Rithmic resolution=String", ErrorClass.None, "EXPECTED_PROVIDER_MATCH");
            Add("M3", "M3.ACCOUNT_SURFACE", DiagnosticStatus.PASS, "accounts=1 orderSnapshots=2", ErrorClass.None, "ACCOUNT_SURFACE_OBSERVED");
            Add("M3", "M3.F05_ORDER_FILL_CONTINUITY", DiagnosticStatus.INCONCLUSIVE, "unmatchedTerminalOrders=0 unmatchedExecutions=0", ErrorClass.None, "ORDER_FILL_CONTINUITY_INCOMPLETE");
            Add("M4", "M4.D078_APPROVAL", DiagnosticStatus.FAIL, "No D-078 operator field approval is recorded.", ErrorClass.ApprovalRequired, "D078_FIELD_APPROVAL_NOT_APPROVED");
            Add("M5", "M5.DOM_SURFACE", DiagnosticStatus.PASS, "level1Quote=True lastTrade=True depth=True", ErrorClass.None, "DOM_SURFACE_OBSERVED");
            Add("M5", "M5.LEVEL1", DiagnosticStatus.PASS, "quote=True lastTrade=True", ErrorClass.None, "QUOTE_OBSERVED", "LAST_TRADE_OBSERVED");
            Add("M5", "M5.DEPTH", DiagnosticStatus.PASS, "depthObserved=True depthOperations=42", ErrorClass.None, "DEPTH_OBSERVED", "DEPTH_OPERATIONS=42");
            Add("M5", "M5.SNAPSHOT_SYNC", DiagnosticStatus.FAIL, "sync=42 failure=1 outOfBounds=41", ErrorClass.DataUnavailable, "SNAPSHOT_SYNC_FAILURE", "SYNC=42", "SYNC_FAILURE=1", "OUT_OF_BOUNDS_TOTAL=41");
            Add("M5", "M5.FRESHNESS", DiagnosticStatus.INCONCLUSIVE, "lastDomEventAgeSeconds=0.5 stale=False", ErrorClass.None, "FRESH", "LAST_EVENT_AGE_SECONDS=0.5");
            Add("M6", "M6.TRADE_STREAM", DiagnosticStatus.PASS, "tradeStreamObservable=true buyDerived=118 sellDerived=122 unknown=4", ErrorClass.None, "TRADE_STREAM_OBSERVED");
            Add("M6", "M6.DIRECTION_SOURCE", DiagnosticStatus.INCONCLUSIVE, "provenance=ALL_QUOTE_DERIVED", ErrorClass.None, "PROVENANCE=ALL_QUOTE_DERIVED");
            Add("M6", "M6.UNKNOWN_DIRECTION", DiagnosticStatus.INCONCLUSIVE, "unknown=4 insideSpread=4", ErrorClass.None, "UNKNOWN_INSIDE_SPREAD=4");
            Add("M6", "M6.TRADEDELTA_COMPLETENESS", DiagnosticStatus.INCONCLUSIVE, "completeness=INCOMPLETE reason=UNAVAILABLE_TRADE_DELTA", ErrorClass.None, "COMPLETENESS=INCOMPLETE", "UNAVAILABLE_TRADE_DELTA");
            Add("M6", "M6.RESET_HANDLING", DiagnosticStatus.INCONCLUSIVE, "resetObserved=false resetCount=0", ErrorClass.None, "NO_RESET_OBSERVED", "RESET_COUNT=0");
            Add("M6", "M6.SEQUENCE_CONTINUITY", DiagnosticStatus.PASS, "sequenceGaps=0 sequenceObservable=true sourceTimeBasis=SERVER_TIME_UNVERIFIED", ErrorClass.None, "SEQUENCE_GAPS=0", "SOURCE_SEQUENCE_OBSERVABLE=true");
            Add("M6", "M6.ORDERFLOW_READINESS_VS_STRATEGY_SKIP", DiagnosticStatus.INCONCLUSIVE, "orderFlowReadiness=AVAILABLE resetObserved=false", ErrorClass.None, "ORDERFLOW_READINESS=AVAILABLE", "NO_RESET_OBSERVED", "NOT_DECLARED_CONTRADICTION");
            Add("M7", "M7.CANDIDATE_REJECTION", DiagnosticStatus.INCONCLUSIVE, "allCandidatesInvalid=1234", ErrorClass.None, "ALL_CANDIDATES_INVALID=1234");
            Add("M8", "M8.TRADING_DISABLED", DiagnosticStatus.PASS, "tradingDisabledRuntimeEvidence=true", ErrorClass.None, "TRADING_DISABLED_RUNTIME_EVIDENCE_PRESENT");
            Add("M9", "M9.AUDIT_SURFACE", DiagnosticStatus.PASS, "accounts=1 orderSnapshots=2", ErrorClass.None, "AUDIT_SURFACE_OBSERVED");

            var modules = new List<ModuleStatusView>
            {
                new ModuleStatusView { ModuleId = "M1", Title = "Environment, API & Lifecycle Compatibility", Status = DiagnosticStatus.INCONCLUSIVE, CheckCount = 4, Executed = true },
                new ModuleStatusView { ModuleId = "M2", Title = "Connection & Provider Identity", Status = DiagnosticStatus.PASS, CheckCount = 2, Executed = true },
                new ModuleStatusView { ModuleId = "M3", Title = "Account & Order Lifecycle (F-05)", Status = DiagnosticStatus.INCONCLUSIVE, CheckCount = 2, Executed = true },
                new ModuleStatusView { ModuleId = "M4", Title = "Instrument Attestation / D-078", Status = DiagnosticStatus.FAIL, CheckCount = 1, Executed = true },
                new ModuleStatusView { ModuleId = "M5", Title = "DOM & Market Data", Status = DiagnosticStatus.FAIL, CheckCount = 5, Executed = true },
                new ModuleStatusView { ModuleId = "M6", Title = "TradeDelta / OrderFlow", Status = DiagnosticStatus.INCONCLUSIVE, CheckCount = 7, Executed = true },
                new ModuleStatusView { ModuleId = "M7", Title = "Strategies / Risk Gate", Status = DiagnosticStatus.INCONCLUSIVE, CheckCount = 1, Executed = true },
                new ModuleStatusView { ModuleId = "M8", Title = "Safety / Acceptance", Status = DiagnosticStatus.PASS, CheckCount = 1, Executed = true },
                new ModuleStatusView { ModuleId = "M9", Title = "Post-Trade Audit", Status = DiagnosticStatus.PASS, CheckCount = 1, Executed = true }
            };

            return new DiagnosticRunResult
            {
                Mode = DiagnosticMode.Full,
                ModeId = "full",
                SessionId = sessionId,
                CorrelationId = correlation,
                StartedAtUtc = started,
                FinishedAtUtc = finished,
                FinalState = RunState.Completed,
                Report = report,
                Modules = modules
            };
        }

        private static EvidenceDataset BuildDataset(string sessionId)
        {
            var dataset = new EvidenceDataset
            {
                Provenance = EvidenceProvenance.Sample,
                Environment = new EnvironmentRecord
                {
                    Environment = EnvironmentClass.Simulation,
                    HostVersionObservable = false,
                    ToolBuildMode = BuildIdentity.BuildMode,
                    TargetFramework = "net8.0",
                    UtcOffsetMinutes = 0
                },
                Provider = new ProviderRecord
                {
                    Name = "Rithmic",
                    ResolutionMethod = "String",
                    IsExpectedEvidenceProvider = true,
                    ProviderBuildObservable = false
                }
            };

            dataset.Connections.Add(new ConnectionRecord
            {
                ConnectionName = "SAMPLE-Sim",
                PriceState = ConnectionState.Connected,
                OrderChannelState = ConnectionState.Connected,
                ProviderName = "Rithmic",
                ProviderDetectedByString = true,
                Role = ConnectionRole.Broker
            });

            dataset.Accounts.Add(new AccountRecord
            {
                AccountRef = "SAMPLE",
                AccountClass = EnvironmentClass.Simulation,
                Denomination = "USD",
                ProviderName = "Rithmic",
                PositionCount = 1,
                ExecutionsInScope = false
            });

            dataset.Instruments.Add(new InstrumentRecord
            {
                FullName = "ES 12-26",
                InstrumentId = "ES 12-26",
                RootSymbol = "ES",
                Expiry = "2026-12-18",
                Exchange = "CME",
                InstrumentType = "Future",
                Currency = "USD",
                TickSize = 0.25,
                PointValue = 50.0,
                MasterMetadataAvailable = true
            });

            dataset.Orders.Add(new OrderRecord
            {
                OrderRef = "sample-order-1",
                InstrumentId = "ES 12-26",
                State = OrderLifecycleState.Filled,
                Quantity = 1,
                FilledQuantity = 1,
                CorrelationId = "sample000000",
                IngestedAtUtc = new DateTime(2026, 11, 12, 8, 0, 1, DateTimeKind.Utc)
            });

            dataset.Positions.Add(new PositionRecord
            {
                AccountRef = "SAMPLE",
                InstrumentId = "ES 12-26",
                Side = PositionSide.Flat,
                Quantity = 0,
                AveragePrice = 0,
                IngestedAtUtc = new DateTime(2026, 11, 12, 8, 0, 2, DateTimeKind.Utc)
            });

            dataset.MarketData.Add(new MarketDataRecord
            {
                InstrumentId = "ES 12-26",
                Kind = MarketDataKind.Quote,
                Bid = 5000,
                Ask = 5000.25,
                SourceTimeBasis = SourceTimeBasis.ServerTimeUnverified
            });

            dataset.Snapshot = new DiagnosticSnapshot
            {
                Lifecycle = BuildSampleLifecycle(),
                TargetAccountConnection = new TargetAccountConnectionObservation
                {
                    AccountSurfaceObservable = true,
                    ObservedAccountCount = 1,
                    MatchingAccountCount = 1,
                    ConnectionCount = 1,
                    MatchingConnectionCount = 1,
                    AccountRef = SyntheticRef.For("account", "SAMPLE-ACCOUNT"),
                    AccountProviderObservable = true,
                    AccountProviderMatches = true,
                    ConnectionObserved = true,
                    ConnectionNameObservable = true,
                    ConnectionNameMatches = true,
                    ConnectionProviderObservable = true,
                    ConnectionProviderMatches = true
                },
                DomMarketData = new DomMarketDataObservation
                {
                    DataSurfaceObservable = true,
                    DepthObserved = true,
                    LastEventUtc = new DateTime(2026, 11, 12, 8, 0, 3, 500, DateTimeKind.Utc),
                    HostAdapter = new DomCounterBlock
                    {
                        Layer = "HOST_ADAPTER",
                        Observable = true,
                        SnapshotSyncCount = 42,
                        SnapshotSyncFailureCount = 1,
                        UpdateOutOfBoundsCount = 41,
                        InsertOutOfBoundsCount = 1,
                        NativeSnapshotBids = 48,
                        NativeSnapshotAsks = 48,
                        InternalBidLevels = 8,
                        InternalAskLevels = 5
                    },
                    Level1QuoteObserved = true,
                    LastTradeObserved = true
                },
                TradeDelta = new TradeDeltaObservation
                {
                    BuyDerivedCount = 118,
                    SellDerivedCount = 122,
                    UnknownCount = 4,
                    UnknownInsideSpreadCount = 4,
                    TradeStreamObservable = true,
                    SequenceObservable = true,
                    AllDirectionsQuoteDerived = true,
                    TradeDeltaCompleteness = "INCOMPLETE",
                    TradeDeltaUnavailableReason = "UNAVAILABLE_TRADE_DELTA",
                    OrderFlowReadiness = "AVAILABLE",
                    SourceTimeBasis = SourceTimeBasis.ServerTimeUnverified
                },
                StrategyRiskGate = new StrategyRiskGateObservation
                {
                    CandidateRejectionReasons = new Dictionary<string, long> { { "ALL_CANDIDATES_INVALID", 1234 } }
                },
                Safety = new SafetyObservation
                {
                    TradingDisabledRuntimeEvidence = true,
                    ReadOnlyPostureObserved = true,
                    SecurityScan = new SecurityScanObservation
                    {
                        Executed = true,
                        SourceAvailable = true,
                        FilesScanned = 103,
                        RuleCount = RiskPilot.Diagnostics.Nt8.Core.Security.SecurityScanCatalog.RuleCount,
                        TradingDisabledObservable = true,
                        TradingDisabledDeclared = true,
                        IdentityDefinitionPresent = true,
                        IdentityDefinitionReviewed = true,
                        ReasonCodes = new List<string> { RiskPilot.Diagnostics.Nt8.Core.Security.SecurityScanReasonCodes.Passed }
                    },
                    ReportSecretFree = true
                },
                Attestation = new InstrumentAttestationObservation
                {
                    HostAvailable = true,
                    RequestedInstrumentId = "ES 12-26",
                    CanonicalInstrumentId = "ES_FUTURES_CME_DEC26",
                    D096RouteCandidateSatisfied = true,
                    RegistryMatched = true,
                    D078Approved = false,
                    Metadata = new InstrumentMetadataObservation
                    {
                        FullName = "ES 12-26",
                        ExpiryRaw = "2026-12-18",
                        ExchangeRaw = "CME",
                        RootName = "ES",
                        InstrumentTypeRaw = "Future",
                        CurrencyRaw = "USD",
                        PointValue = 50.0,
                        TickSize = 0.25,
                        TickValue = 12.5,
                        MasterMetadataAvailable = true
                    },
                    FailureReasons = new List<string> { RiskPilot.Diagnostics.Nt8.Core.Engine.AttestationReasonCodes.D078FieldApprovalNotApproved }
                }
            };

            return dataset;
        }

        /// <summary>
        /// A synthetic, clearly-SAMPLE lifecycle surface so the sample package shows the M1
        /// lifecycle block. It mirrors the AddOn's own startup progression; the package is still
        /// marked SAMPLE and must never be read as a runtime result.
        /// </summary>
        private static LifecycleObservation BuildSampleLifecycle()
        {
            var lifecycle = new LifecycleObservation { LifecycleSurfaceObservable = true };
            lifecycle.ObservedStates.Add("SetDefaults");
            lifecycle.ObservedStates.Add("Configure");
            lifecycle.ObservedStates.Add("Active");
            lifecycle.ObservedStates.Add("DataLoaded");
            lifecycle.ObservedStates.Add("Realtime");
            return lifecycle;
        }
    }
}
