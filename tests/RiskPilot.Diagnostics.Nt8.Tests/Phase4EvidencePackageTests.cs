using System;
using System.Collections.Generic;
using System.Globalization;
using RiskPilot.Diagnostics.Nt8.Core.Evidence;
using RiskPilot.Diagnostics.Nt8.Core.Models;
using RiskPilot.Diagnostics.Nt8.Core.Models.Observations;
using RiskPilot.Diagnostics.Nt8.Core.Report;
using RiskPilot.Diagnostics.Nt8.Core.Result;
using RiskPilot.Diagnostics.Nt8.Core.Run;
using RiskPilot.Diagnostics.Nt8.Core.Security;
using RiskPilot.Diagnostics.Nt8.Core.Status;
using Xunit;

namespace RiskPilot.Diagnostics.Nt8.Tests
{
    /// <summary>
    /// Phase 4 evidence-package tests. They assert that every one of the 17 artifacts is
    /// produced, that the manifest is internally consistent (hashes and lengths match the
    /// bytes actually written), that CSV/JSON/HTML outputs are well-formed, that missing data
    /// is reported as unavailable rather than fabricated, and that no secret can reach an
    /// artifact.
    /// </summary>
    public class Phase4EvidencePackageTests
    {
        private static DiagnosticRunResult RunResult()
        {
            var report = new DiagnosticReport
            {
                SessionId = "rp-20260101-000000-abc123",
                CorrelationId = "corr00000000",
                GeneratedAtUtc = new DateTime(2026, 1, 1, 0, 0, 1, DateTimeKind.Utc)
            };
            report.Add(DiagnosticResult.Pass("M1", "M1.CHECK", "corr00000000", DateTime.UtcNow).WithFindingId("M1.CHECK"));
            report.Add(DiagnosticResult.Fail("M3", "M3.CHECK", ErrorClass.DataUnavailable, "corr00000000", DateTime.UtcNow)
                .WithFindingId("M3.CHECK").WithObservation("order mismatch"));
            report.AddFinding(new DiagnosticFinding
            {
                CheckId = "M3.CHECK",
                ModuleId = "M3",
                FindingId = "M3.CHECK",
                Status = DiagnosticStatus.FAIL,
                Severity = DiagnosticSeverity.High,
                Observation = "order mismatch",
                Source = "NinjaTrader Public API",
                Method = "read-only",
                Limitation = "terminal orders may be pruned",
                NextAction = "capture evidence",
                ObservedAtUtc = new DateTime(2026, 1, 1, 0, 0, 1, DateTimeKind.Utc)
            });

            var modules = new List<ModuleStatusView>
            {
                new ModuleStatusView { ModuleId = "M1", Title = "M1", Status = DiagnosticStatus.PASS, CheckCount = 1, Executed = true },
                new ModuleStatusView { ModuleId = "M3", Title = "M3", Status = DiagnosticStatus.FAIL, CheckCount = 1, Executed = true }
            };

            return new DiagnosticRunResult
            {
                Mode = DiagnosticMode.Full,
                ModeId = "full",
                SessionId = "rp-20260101-000000-abc123",
                CorrelationId = "corr00000000",
                StartedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                FinishedAtUtc = new DateTime(2026, 1, 1, 0, 0, 2, DateTimeKind.Utc),
                FinalState = RunState.Completed,
                Report = report,
                Modules = modules
            };
        }

        private static StrategyRiskGateObservation BuildObservedStrategyRiskGate()
        {
            var observation = new StrategyRiskGateObservation();
            observation.Strategies.Add(new StrategyEvaluationObservation
            {
                StrategyId = "STRAT_SCALPING_CANDIDATE_01",
                RecordObserved = true,
                AvailableObservable = true,
                Available = true,
                CandidateCountsObservable = true,
                CandidateCount = 4,
                ValidCandidateCount = 1,
                InvalidCandidateCount = 3
            });
            observation.CandidateRejectionReasons["ALL_CANDIDATES_INVALID"] = 1;
            return observation;
        }

        private static EvidenceDataset FullDataset()
        {
            var dataset = new EvidenceDataset
            {
                Environment = new EnvironmentRecord
                {
                    Environment = EnvironmentClass.Simulation,
                    HostVersion = "8.1.8.4",
                    HostVersionObservable = false,
                    ToolBuildMode = "StubOffline",
                    TargetFramework = "net8.0",
                    UtcOffsetMinutes = 0
                },
                Provider = new ProviderRecord
                {
                    Name = "Rithmic",
                    ResolutionMethod = "string-compare",
                    IsExpectedEvidenceProvider = true,
                    ProviderBuildObservable = false,
                    UnobservableReason = "no public API"
                }
            };
            dataset.Connections.Add(new ConnectionRecord
            {
                ConnectionName = "Sim101",
                PriceState = ConnectionState.Connected,
                OrderChannelState = ConnectionState.Connected,
                ProviderName = "Rithmic",
                ProviderDetectedByString = true,
                Role = ConnectionRole.Broker,
                ReconnectCount = 0
            });
            dataset.Accounts.Add(new AccountRecord
            {
                AccountRef = "ACC-0001",
                AccountClass = EnvironmentClass.Simulation,
                Denomination = "USD",
                ProviderName = "Rithmic",
                PositionCount = 1,
                WorkingOrderCount = 2,
                ExecutionsInScope = true
            });
            dataset.Instruments.Add(new InstrumentRecord
            {
                FullName = "ES 03-26",
                InstrumentId = "ES 03-26",
                RootSymbol = "ES",
                Expiry = "2026-03-20",
                Exchange = "CME",
                InstrumentType = "Future",
                Currency = "USD",
                TickSize = 0.25,
                PointValue = 50.0,
                MasterMetadataAvailable = true
            });
            dataset.Orders.Add(new OrderRecord
            {
                OrderRef = "ord-1",
                InstrumentId = "ES 03-26",
                State = OrderLifecycleState.Filled,
                Quantity = 1,
                FilledQuantity = 1,
                CorrelationId = "corr00000000",
                IngestedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            });
            dataset.Executions.Add(new ExecutionRecord
            {
                ExecutionRef = "exec-1",
                OrderRef = "ord-1",
                InstrumentId = "ES 03-26",
                Price = 5000.25,
                Quantity = 1,
                PositionSide = PositionSide.Long,
                CorrelationId = "corr00000000",
                IngestedAtUtc = new DateTime(2026, 1, 1, 0, 0, 1, DateTimeKind.Utc)
            });
            dataset.Positions.Add(new PositionRecord
            {
                AccountRef = "ACC-0001",
                InstrumentId = "ES 03-26",
                Side = PositionSide.Long,
                Quantity = 1,
                AveragePrice = 5000.25,
                IngestedAtUtc = new DateTime(2026, 1, 1, 0, 0, 2, DateTimeKind.Utc)
            });
            dataset.MarketData.Add(new MarketDataRecord { InstrumentId = "ES 03-26", Kind = MarketDataKind.Quote, Bid = 5000, Ask = 5000.25 });
            dataset.Dom.Add(new DomRecord { InstrumentId = "ES 03-26", Operation = DomOperation.Insert, Position = 0, Price = 5000, Volume = 5 });
            dataset.Strategies.Add(new StrategyRecord { StrategyId = "RiskPilot", Activity = StrategyActivity.Active });

            dataset.Snapshot = new DiagnosticSnapshot
            {
                DomMarketData = new DomMarketDataObservation
                {
                    HostAdapter = new DomCounterBlock { Layer = "host", Observable = true, InsertCount = 1 },
                    Recorder = new DomCounterBlock { Layer = "recorder", Observable = true, InsertCount = 1 },
                    Level1QuoteObserved = true,
                    LastTradeObserved = true,
                    LastDomEventAgeSeconds = 0.5,
                    MalformedCount = 0,
                    SequenceGapCount = 0,
                    DroppedEventCount = 0
                },
                TradeDelta = new TradeDeltaObservation
                {
                    BuyDerivedCount = 3,
                    SellDerivedCount = 2,
                    UnknownCount = 0,
                    OrderFlowReadiness = "READY",
                    TradeDeltaCompleteness = "COMPLETE",
                    SourceTimeBasis = SourceTimeBasis.ServerTimeUnverified
                },
                StrategyRiskGate = BuildObservedStrategyRiskGate(),
                Safety = new SafetyObservation
                {
                    TradingDisabledRuntimeEvidence = true,
                    ReadOnlyPostureObserved = true,
                    StaticScanPassed = true,
                    StaticScanForbiddenApiCount = 0,
                    StaticScanFileCount = 103,
                    ReportSecretFree = true
                },
                Attestation = new InstrumentAttestationObservation
                {
                    HostAvailable = true,
                    RequestedInstrumentId = "ES 03-26",
                    CanonicalInstrumentId = "ES 03-26",
                    D096RouteCandidateSatisfied = true,
                    RegistryMatched = true,
                    D078Approved = true
                }
            };

            return dataset;
        }

        [Fact]
        public void Build_ProducesAll17Artifacts()
        {
            var package = EvidencePackageBuilder.Build(RunResult(), FullDataset());
            Assert.True(package.HasCompleteArtifactSet());
            Assert.Equal(17, package.Files.Count);
            foreach (var name in EvidencePackageCatalog.All)
            {
                Assert.True(package.Contains(name), "missing artifact: " + name);
            }
        }

        [Fact]
        public void Build_Manifest_IsInternallyConsistent()
        {
            var package = EvidencePackageBuilder.Build(RunResult(), FullDataset());
            Assert.True(package.VerifyIntegrity(out var failure), failure);

            // The manifest must list 16 files (all but manifest.json itself).
            Assert.Equal(16, package.Manifest.Files.Count);
            Assert.Equal("rp-20260101-000000-abc123", package.Manifest.SessionId);
            Assert.Equal("full", package.Manifest.ModeId);
            Assert.Equal("UNOBSERVABLE", package.Manifest.NtRuntimeVersion);
            Assert.Equal("FAIL", package.Manifest.AggregateStatus);
            Assert.False(string.IsNullOrEmpty(package.Manifest.ManifestSha256));
        }

        [Fact]
        public void Build_Manifest_HashesMatchWrittenBytes()
        {
            var package = EvidencePackageBuilder.Build(RunResult(), FullDataset());
            foreach (var entry in package.Manifest.Files)
            {
                var content = package.Read(entry.Name);
                Assert.Equal(Hashing.Sha256Hex(content), entry.Sha256);
                Assert.Equal(System.Text.Encoding.UTF8.GetByteCount(content), entry.ByteLength);
            }
        }

        [Fact]
        public void Build_Manifest_MasksAccounts()
        {
            var package = EvidencePackageBuilder.Build(RunResult(), FullDataset());
            Assert.Single(package.Manifest.AccountsObserved);
            var masked = package.Manifest.AccountsObserved[0].MaskedRef;
            Assert.NotEqual("ACC-0001", masked);
            Assert.StartsWith("account#", masked);
        }

        [Fact]
        public void EmptyDataset_ReportsUnavailable_NotFabricated()
        {
            var package = EvidencePackageBuilder.Build(RunResult(), new EvidenceDataset());
            Assert.True(package.VerifyIntegrity(out var failure), failure);

            var connections = package.Read(EvidencePackageCatalog.ConnectionsJson);
            Assert.Contains("UNAVAILABLE", connections);

            var accounts = package.Read(EvidencePackageCatalog.AccountsJson);
            Assert.Contains("UNAVAILABLE", accounts);

            var dom = package.Read(EvidencePackageCatalog.DomSummaryJson);
            Assert.Contains("UNAVAILABLE", dom);
        }

        [Fact]
        public void AccountsJson_ReportsIdentityVerification_WithNoRawIdentity()
        {
            // A fully matching identity is reported as PASS in the accounts artifact, and the
            // artifact never carries the raw account/connection names.
            var dataset = new EvidenceDataset();
            dataset.Snapshot = new DiagnosticSnapshot
            {
                AccountIdentity = new AccountIdentityObservation
                {
                    AccountSurfaceObservable = true,
                    ObservedAccountCount = 1,
                    MatchingAccountCount = 1,
                    AccountRef = SyntheticRef.For("account", "SimRP.BOT"),
                    AccountProviderObservable = true,
                    AccountProviderMatches = true,
                    ConnectionObserved = true,
                    ConnectionNameObservable = true,
                    ConnectionNameMatches = true,
                    ConnectionProviderObservable = true,
                    ConnectionProviderMatches = true,
                    ConnectionStatusObservable = true,
                    ConnectionConnected = true,
                    ModeObservable = true,
                    ModeIsSimulation = true,
                    DenominationObservable = true,
                    DenominationIsUsd = true
                }
            };

            var package = EvidencePackageBuilder.Build(RunResult(), dataset);
            Assert.True(package.VerifyIntegrity(out var failure), failure);

            var accounts = package.Read(EvidencePackageCatalog.AccountsJson);
            Assert.Contains("\"identityVerification\"", accounts);
            Assert.Contains("ACCOUNT_IDENTITY_MATCH", accounts);
            Assert.Contains("\"status\":\"PASS\"", accounts);

            Assert.DoesNotContain("SimRP.BOT", accounts);
            Assert.DoesNotContain("My Rithmic", accounts);
        }

        [Fact]
        public void AccountsJson_IdentityVerification_IsInconclusive_WhenUnobservable()
        {
            var package = EvidencePackageBuilder.Build(RunResult(), new EvidenceDataset());
            var accounts = package.Read(EvidencePackageCatalog.AccountsJson);
            Assert.Contains("\"identityVerification\"", accounts);
            Assert.Contains("ACCOUNT_IDENTITY_UNOBSERVABLE", accounts);
        }

        [Fact]
        public void InstrumentsJson_ReportsInstrumentIdentity_WithNoRawSymbol()
        {
            // A fully matching ES/CME identity is reported as PASS in the instruments artifact, and
            // the artifact never carries the raw contract symbol.
            var dataset = new EvidenceDataset();
            dataset.Snapshot = new DiagnosticSnapshot
            {
                InstrumentIdentity = new InstrumentIdentityObservation
                {
                    InstrumentSurfaceObservable = true,
                    ObservedInstrumentCount = 1,
                    RootMetadataObservable = true,
                    MatchingInstrumentCount = 1,
                    InstrumentRef = SyntheticRef.For("instrument", "ES 12-26"),
                    RootSymbolObservable = true,
                    RootSymbolMatches = true,
                    InstrumentTypeObservable = true,
                    InstrumentTypeMatches = true,
                    CurrencyObservable = true,
                    CurrencyMatches = true,
                    SymbolParsed = true,
                    CanonicalInstrumentId = "ES_FUTURES_CME_DEC26",
                    CanonicalIdObservable = true,
                    CanonicalIdMatches = true,
                    ExchangeMetadataObservable = true,
                    SelectedRouteIsGlobex = true,
                    ConfiguredExchangesAreGlobexOnly = true,
                    ProviderRouteIsRithmicOnly = true,
                    ExchangeConfirmedCme = true
                }
            };

            var package = EvidencePackageBuilder.Build(RunResult(), dataset);
            Assert.True(package.VerifyIntegrity(out var failure), failure);

            var instruments = package.Read(EvidencePackageCatalog.InstrumentsJson);
            Assert.Contains("\"instrumentIdentity\"", instruments);
            Assert.Contains("INSTRUMENT_IDENTITY_MATCH", instruments);
            Assert.Contains("\"status\":\"PASS\"", instruments);
            Assert.DoesNotContain("ES 12-26", instruments);
        }

        [Fact]
        public void InstrumentsJson_InstrumentIdentity_IsInconclusive_WhenUnobservable()
        {
            var package = EvidencePackageBuilder.Build(RunResult(), new EvidenceDataset());
            var instruments = package.Read(EvidencePackageCatalog.InstrumentsJson);
            Assert.Contains("\"instrumentIdentity\":\"UNOBSERVABLE\"", instruments);
        }

        [Fact]
        public void PartialDataset_ReportsOnlyObservedSectionsAvailable()
        {
            // Only connections + accounts are observable; every other section must be
            // reported UNAVAILABLE with a reason, never fabricated.
            var dataset = new EvidenceDataset();
            dataset.Connections.Add(new ConnectionRecord
            {
                ConnectionName = "Sim101",
                PriceState = ConnectionState.Connected,
                OrderChannelState = ConnectionState.Connected,
                ProviderName = "Rithmic",
                Role = ConnectionRole.Broker
            });
            dataset.Accounts.Add(new AccountRecord
            {
                AccountRef = "ACC-0001",
                AccountClass = EnvironmentClass.Simulation,
                ProviderName = "Rithmic"
            });

            var package = EvidencePackageBuilder.Build(RunResult(), dataset);
            Assert.True(package.VerifyIntegrity(out var failure), failure);

            Assert.Contains("AVAILABLE", package.Read(EvidencePackageCatalog.ConnectionsJson));
            Assert.Contains("AVAILABLE", package.Read(EvidencePackageCatalog.AccountsJson));
            Assert.Contains("UNAVAILABLE", package.Read(EvidencePackageCatalog.EnvironmentJson));
            Assert.Contains("UNAVAILABLE", package.Read(EvidencePackageCatalog.DomSummaryJson));
            Assert.Contains("UNAVAILABLE", package.Read(EvidencePackageCatalog.OrderFlowSummaryJson));
            Assert.Contains("UNAVAILABLE", package.Read(EvidencePackageCatalog.SafetyAuditJson));
        }

        [Fact]
        public void MarketDataSummary_DerivesSourceTimeBasis_NotHardcoded()
        {
            // No market-data records: the basis must be UNOBSERVABLE, not a fabricated claim.
            var empty = EvidencePackageBuilder.Build(RunResult(), new EvidenceDataset());
            Assert.Contains("UNOBSERVABLE", empty.Read(EvidencePackageCatalog.MarketDataSummaryJson));

            // Records with an unverified basis: the aggregate must reflect the weakest basis.
            var dataset = new EvidenceDataset();
            dataset.MarketData.Add(new MarketDataRecord { Kind = MarketDataKind.Trade, SourceTimeBasis = SourceTimeBasis.ServerTimeUnverified });
            dataset.MarketData.Add(new MarketDataRecord { Kind = MarketDataKind.Quote, SourceTimeBasis = SourceTimeBasis.ExchangeTimeVerified });
            var package = EvidencePackageBuilder.Build(RunResult(), dataset);
            Assert.Contains("SERVER_TIME_UNVERIFIED", package.Read(EvidencePackageCatalog.MarketDataSummaryJson));
        }

        [Fact]
        public void TruncatedDataset_IsReportedInManifest()
        {
            var dataset = FullDataset();
            dataset.TruncationReason = "EVIDENCE_COLLECTOR_TRUNCATED: dropped 5 record(s).";
            var package = EvidencePackageBuilder.Build(RunResult(), dataset);
            Assert.True(package.Manifest.Truncated);
            Assert.Contains("dropped", package.Manifest.TruncationReason);
        }

        [Fact]
        public void CsvArtifacts_AreWellFormed_AndHaveHeaders()
        {
            var package = EvidencePackageBuilder.Build(RunResult(), FullDataset());

            var orders = package.Read(EvidencePackageCatalog.OrdersCsv);
            var lines = orders.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal(2, lines.Length); // header + one order
            Assert.StartsWith("orderRef,", lines[0]);
            Assert.Contains("ord-1", lines[1]);

            var executions = package.Read(EvidencePackageCatalog.ExecutionsCsv);
            Assert.Contains("exec-1", executions);

            var positions = package.Read(EvidencePackageCatalog.PositionsCsv);
            Assert.Contains("5000.25", positions); // invariant culture
            Assert.DoesNotContain("5000,25", positions);
        }

        [Fact]
        public void CsvArtifacts_AreWellFormed_WhenEmpty()
        {
            var package = EvidencePackageBuilder.Build(RunResult(), new EvidenceDataset());
            var orders = package.Read(EvidencePackageCatalog.OrdersCsv);
            var lines = orders.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
            Assert.Single(lines); // header only, never a fabricated row
        }

        [Fact]
        public void HtmlArtifacts_Render_AndEscapeContent()
        {
            var result = RunResult();
            result.Report.AddFinding(new DiagnosticFinding
            {
                CheckId = "M1.X",
                ModuleId = "M1",
                FindingId = "M1.X",
                Status = DiagnosticStatus.FAIL,
                Severity = DiagnosticSeverity.High,
                Observation = "<script>alert('x')</script>",
                ObservedAtUtc = DateTime.UtcNow
            });

            var package = EvidencePackageBuilder.Build(result, FullDataset());
            var summary = package.Read(EvidencePackageCatalog.SummaryHtml);
            var full = package.Read(EvidencePackageCatalog.FullReportHtml);

            Assert.Contains("<!DOCTYPE html>", summary);
            Assert.Contains("</html>", summary);
            Assert.Contains("<!DOCTYPE html>", full);
            Assert.DoesNotContain("<script>alert", full); // escaped
            Assert.Contains("&lt;script&gt;", full);
        }

        [Fact]
        public void AcceptanceMatrix_CarriesEveryResult()
        {
            var package = EvidencePackageBuilder.Build(RunResult(), FullDataset());
            var matrix = package.Read(EvidencePackageCatalog.AcceptanceMatrixCsv);
            Assert.Contains("M1.CHECK", matrix);
            Assert.Contains("M3.CHECK", matrix);
            Assert.Contains("FAIL", matrix);
        }

        [Fact]
        public void Findings_CarryAllRequiredFields()
        {
            var package = EvidencePackageBuilder.Build(RunResult(), FullDataset());
            var findings = package.Read(EvidencePackageCatalog.FindingsJson);
            Assert.Contains("\"findingId\"", findings);
            Assert.Contains("\"severity\"", findings);
            Assert.Contains("\"observation\"", findings);
            Assert.Contains("\"collectionMethod\"", findings);
            Assert.Contains("\"limitation\"", findings);
            Assert.Contains("\"recommendedNextAction\"", findings);
            Assert.Contains("\"evidenceReference\"", findings);
        }

        [Fact]
        public void Package_IsSecretFree()
        {
            var package = EvidencePackageBuilder.Build(RunResult(), FullDataset());
            Assert.True(package.IsSecretFree(out var offender), "secret found in " + offender);
            Assert.True(package.ManifestIsIdentitySafe());
        }

        [Fact]
        public void Package_RedactsSecretsInjectedIntoObservations()
        {
            var result = RunResult();
            result.Report.AddFinding(new DiagnosticFinding
            {
                CheckId = "M1.LEAK",
                ModuleId = "M1",
                FindingId = "M1.LEAK",
                Status = DiagnosticStatus.INCONCLUSIVE,
                Severity = DiagnosticSeverity.Low,
                Observation = "password=SuperSecret123 token=abcdef0123456789",
                ObservedAtUtc = DateTime.UtcNow
            });

            var package = EvidencePackageBuilder.Build(result, FullDataset());
            var findings = package.Read(EvidencePackageCatalog.FindingsJson);
            Assert.DoesNotContain("SuperSecret123", findings);
            Assert.DoesNotContain("abcdef0123456789", findings);
        }

        [Fact]
        public void VerifyIntegrity_DetectsTampering()
        {
            var package = EvidencePackageBuilder.Build(RunResult(), FullDataset());
            var tampered = new Dictionary<string, string>(package.Files, StringComparer.Ordinal);
            tampered[EvidencePackageCatalog.FindingsJson] = tampered[EvidencePackageCatalog.FindingsJson] + "tampered";

            var rehydrated = new EvidencePackage(package.Manifest, tampered);
            Assert.False(rehydrated.VerifyIntegrity(out var failure));
            Assert.Contains("hash mismatch", failure);
        }

        [Fact]
        public void Manifest_Json_IsWellFormedAndSchemaVersioned()
        {
            var package = EvidencePackageBuilder.Build(RunResult(), FullDataset());
            var manifest = package.Read(EvidencePackageCatalog.ManifestJson);
            Assert.Contains("\"schemaVersion\"", manifest);
            Assert.Contains("\"files\"", manifest);
            Assert.Contains("\"sha256\"", manifest);
            Assert.Contains("\"droppedCount\"", manifest);
            Assert.Contains("\"truncated\"", manifest);
        }
    }
}
