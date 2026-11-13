using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using RiskPilot.Diagnostics.Nt8.Core.Engine;
using RiskPilot.Diagnostics.Nt8.Core.Engine.Probes;
using RiskPilot.Diagnostics.Nt8.Core.Evidence;
using RiskPilot.Diagnostics.Nt8.Core.Models;
using RiskPilot.Diagnostics.Nt8.Core.Models.Observations;
using RiskPilot.Diagnostics.Nt8.Core.Queue;
using RiskPilot.Diagnostics.Nt8.Core.Report;
using RiskPilot.Diagnostics.Nt8.Core.Result;
using RiskPilot.Diagnostics.Nt8.Core.Security;
using RiskPilot.Diagnostics.Nt8.Core.Session;
using RiskPilot.Diagnostics.Nt8.Core.Status;
using RiskPilot.Diagnostics.Nt8.Host;
using RiskPilot.Diagnostics.Nt8.Tests.Fakes;
using Xunit;

namespace RiskPilot.Diagnostics.Nt8.Tests
{
    /// <summary>
    /// Phase 5 acceptance scenarios. Each test maps to one of the mandatory review scenarios
    /// (provider identity, connection loss, account without orders, invalid order id,
    /// incomplete order lifecycle, unmatched execution, incomplete instrument metadata,
    /// missing D-078 approval, DOM reset / out-of-bounds / snapshot mismatch, unknown trade
    /// direction, incomplete TradeDelta, unavailable OrderFlow, missing strategy capability,
    /// queue overflow, shutdown mid-capture, report integrity, trading disabled).
    /// <para>
    /// They drive the real production probes, pipeline and runtime; the only doubles are the
    /// deterministic clock and the in-memory data provider.
    /// </para>
    /// </summary>
    public class Phase5ScenarioTests
    {
        private static ProbeContext Context(FakeDataProvider provider)
        {
            var session = new SessionContext(SessionId.New(DateTime.UtcNow), DateTime.UtcNow);
            return new ProbeContext(provider, session, new FakeClock(DateTime.UtcNow), "corr00000000");
        }

        private static DiagnosticResult Find(IEnumerable<DiagnosticResult> results, string checkId)
        {
            foreach (var result in results)
            {
                if (result.CheckId == checkId)
                {
                    return result;
                }
            }

            throw new InvalidOperationException("check not found: " + checkId);
        }

        private static DiagnosticRuntime Runtime(FakeDataProvider provider, int queueCapacity)
        {
            var runtime = new DiagnosticRuntime(provider, new FakeClock(DateTime.UtcNow), queueCapacity, 64, 512);
            runtime.Initialize();
            return runtime;
        }

        // ---------------- M2: provider identity + connection loss ----------------

        [Fact]
        public void Scenario_ProviderRithmic_IsReportedAsExpectedProvider()
        {
            var provider = new FakeDataProvider
            {
                Provider = new ProviderRecord { Name = "Rithmic", ResolutionMethod = "String", IsExpectedEvidenceProvider = true },
                Snapshot = new DiagnosticSnapshot
                {
                    Provider = new ProviderEvidence { RawProviderValue = "Rithmic", ResolutionMethod = "String", ResolutionBasis = ProviderResolutionBasis.SingleConnection, MatchesExpectedProvider = true }
                }
            };
            provider.Connections.Add(new ConnectionRecord
            {
                ProviderName = "Rithmic",
                PriceState = ConnectionState.Connected,
                OrderChannelState = ConnectionState.Connected,
                Role = ConnectionRole.Broker
            });

            var results = new M2ConnectionProbe().Evaluate(Context(provider));
            Assert.Equal(DiagnosticStatus.PASS, Find(results, "M2.PROVIDER_IDENTITY").Status);
        }

        [Fact]
        public void Scenario_ProviderUnknown_IsInconclusiveNotPass()
        {
            var provider = new FakeDataProvider
            {
                Provider = new ProviderRecord { Name = "UnknownVendor", IsExpectedEvidenceProvider = false },
                Snapshot = new DiagnosticSnapshot
                {
                    Provider = new ProviderEvidence { RawProviderValue = "UnknownVendor", ResolutionMethod = "String", ResolutionBasis = ProviderResolutionBasis.SingleConnection, MatchesExpectedProvider = false }
                }
            };
            provider.Connections.Add(new ConnectionRecord
            {
                ProviderName = "UnknownVendor",
                PriceState = ConnectionState.Connected,
                OrderChannelState = ConnectionState.Connected,
                Role = ConnectionRole.Broker
            });

            var results = new M2ConnectionProbe().Evaluate(Context(provider));
            Assert.Equal(DiagnosticStatus.INCONCLUSIVE, Find(results, "M2.PROVIDER_IDENTITY").Status);
        }

        [Fact]
        public void Scenario_ConnectionLost_DoesNotPassTheFeedCheck()
        {
            var provider = new FakeDataProvider();
            provider.Connections.Add(new ConnectionRecord
            {
                ProviderName = "Rithmic",
                PriceState = ConnectionState.ConnectionLost,
                OrderChannelState = ConnectionState.Disconnected,
                Role = ConnectionRole.Broker
            });

            var results = new M2ConnectionProbe().Evaluate(Context(provider));
            Assert.Equal(DiagnosticStatus.INCONCLUSIVE, Find(results, "M2.CONNECTION.BROKER").Status);
        }

        [Fact]
        public void Scenario_ReconnectedFeeds_PassTheFeedCheck()
        {
            var provider = new FakeDataProvider();
            provider.Connections.Add(new ConnectionRecord
            {
                ProviderName = "Rithmic",
                PriceState = ConnectionState.Connected,
                OrderChannelState = ConnectionState.Connected,
                Role = ConnectionRole.Broker,
                ReconnectCount = 2
            });

            var results = new M2ConnectionProbe().Evaluate(Context(provider));
            Assert.Equal(DiagnosticStatus.PASS, Find(results, "M2.CONNECTION.BROKER").Status);
            Assert.Contains("RECONNECT_OBSERVED", Find(results, "M2.RECONNECT").ReasonCodes);
        }

        // ---------------- M3: account without orders, invalid id, incomplete lifecycle ----------------

        [Fact]
        public void Scenario_AccountWithoutOrders_IsInconclusiveNotPass()
        {
            var provider = new FakeDataProvider
            {
                Snapshot = new DiagnosticSnapshot
                {
                    OrderLifecycle = new OrderLifecycleObservation
                    {
                        OrderSurfaceObservable = true,
                        AccountCount = 1,
                        OrderSnapshotCount = 0,
                        ExecutionsInScope = true
                    }
                }
            };

            var results = new M3AccountOrderProbe().Evaluate(Context(provider));
            Assert.Equal(DiagnosticStatus.INCONCLUSIVE, Find(results, "M3.ORDERID_CLASSIFICATION").Status);
            Assert.Equal(DiagnosticStatus.INCONCLUSIVE, Find(results, "M3.F05_ORDER_FILL_CONTINUITY").Status);
        }

        [Fact]
        public void Scenario_WhitespaceOrderId_IsMalformedAndFails()
        {
            var provider = new FakeDataProvider
            {
                Snapshot = new DiagnosticSnapshot
                {
                    OrderLifecycle = new OrderLifecycleObservation
                    {
                        OrderSurfaceObservable = true,
                        OrderSnapshotCount = 1,
                        OrderIdClassCounts = new Dictionary<OrderIdClass, int> { { OrderIdClass.Whitespace, 1 } }
                    }
                }
            };

            var results = new M3AccountOrderProbe().Evaluate(Context(provider));
            Assert.Equal(DiagnosticStatus.FAIL, Find(results, "M3.ORDERID_CLASSIFICATION").Status);
        }

        [Fact]
        public void Scenario_IncompleteOrderLifecycle_KeepsCorrelationInconclusive()
        {
            var provider = new FakeDataProvider
            {
                Snapshot = new DiagnosticSnapshot
                {
                    OrderLifecycle = new OrderLifecycleObservation
                    {
                        OrderSurfaceObservable = true,
                        OrderSnapshotCount = 2,
                        OrderUpdateCount = 1,
                        ExecutionsInScope = true,
                        UnmatchedTerminalOrderCount = 1,
                        OrderIdClassCounts = new Dictionary<OrderIdClass, int> { { OrderIdClass.Valid, 2 } }
                    }
                }
            };

            var results = new M3AccountOrderProbe().Evaluate(Context(provider));
            Assert.Equal(DiagnosticStatus.INCONCLUSIVE, Find(results, "M3.CORRELATION").Status);
            Assert.Equal(DiagnosticStatus.INCONCLUSIVE, Find(results, "M3.F05_ORDER_FILL_CONTINUITY").Status);
        }

        [Fact]
        public void Scenario_UnmatchedExecution_KeepsCorrelationInconclusive()
        {
            var provider = new FakeDataProvider
            {
                Snapshot = new DiagnosticSnapshot
                {
                    OrderLifecycle = new OrderLifecycleObservation
                    {
                        OrderSurfaceObservable = true,
                        OrderSnapshotCount = 1,
                        ExecutionsInScope = true,
                        UnmatchedExecutionCount = 1,
                        OrderIdClassCounts = new Dictionary<OrderIdClass, int> { { OrderIdClass.Valid, 1 } }
                    }
                }
            };

            var results = new M3AccountOrderProbe().Evaluate(Context(provider));
            Assert.Equal(DiagnosticStatus.INCONCLUSIVE, Find(results, "M3.CORRELATION").Status);
        }

        // ---------------- M4: incomplete instrument metadata + D-078 ----------------

        [Fact]
        public void Scenario_IncompleteInstrumentMetadata_FailsFieldMetadata()
        {
            var provider = new FakeDataProvider
            {
                Snapshot = new DiagnosticSnapshot
                {
                    Attestation = new InstrumentAttestationObservation
                    {
                        HostAvailable = true,
                        Metadata = new InstrumentMetadataObservation
                        {
                            FullName = "ES 12-26",
                            MasterMetadataAvailable = false,
                            ReadFailures = new List<string> { "MasterInstrument" }
                        },
                        RegistryMatched = true,
                        D078Approved = false,
                        FailureReasons = new List<string> { Core.Engine.AttestationReasonCodes.D078FieldApprovalNotApproved }
                    }
                }
            };

            var results = new M4InstrumentAttestationProbe().Evaluate(Context(provider));
            Assert.Equal(DiagnosticStatus.FAIL, Find(results, "M4.FIELD_METADATA").Status);
        }

        [Fact]
        public void Scenario_MissingD078Approval_FailsClosed()
        {
            var provider = new FakeDataProvider
            {
                Snapshot = new DiagnosticSnapshot
                {
                    Attestation = new InstrumentAttestationObservation
                    {
                        HostAvailable = true,
                        Metadata = new InstrumentMetadataObservation { FullName = "ES 12-26", MasterMetadataAvailable = true },
                        RegistryMatched = true,
                        D078Approved = false,
                        FailureReasons = new List<string> { Core.Engine.AttestationReasonCodes.D078FieldApprovalNotApproved }
                    }
                }
            };

            var results = new M4InstrumentAttestationProbe().Evaluate(Context(provider));
            Assert.Equal(DiagnosticStatus.FAIL, Find(results, "M4.D078_APPROVAL").Status);
        }

        // ---------------- M5: DOM reset / out-of-bounds / snapshot mismatch ----------------

        [Fact]
        public void Scenario_DomResetAndOutOfBounds_AreReportedAsEvidence()
        {
            var provider = new FakeDataProvider
            {
                Snapshot = new DiagnosticSnapshot
                {
                    DomMarketData = new DomMarketDataObservation
                    {
                        HostAdapter = new DomCounterBlock
                        {
                            Layer = "HOST_ADAPTER",
                            Observable = true,
                            ResetCount = 227,
                            SnapshotSyncCount = 265,
                            SnapshotSyncFailureCount = 1,
                            UpdateOutOfBoundsCount = 264,
                            InsertOutOfBoundsCount = 1,
                            NativeSnapshotBids = 64,
                            NativeSnapshotAsks = 64,
                            InternalBidLevels = 10,
                            InternalAskLevels = 6
                        },
                        Level1QuoteObserved = true,
                        LastTradeObserved = true
                    }
                }
            };

            var results = new M5DomMarketDataProbe().Evaluate(Context(provider));
            var sync = Find(results, "M5.SNAPSHOT_SYNC");
            Assert.Equal(DiagnosticStatus.FAIL, sync.Status);
            Assert.Contains("SYNC_FAILURE=1", sync.ReasonCodes);
            Assert.Contains("UPDATE_OOB=264", sync.ReasonCodes);
            Assert.Contains("INSERT_OOB=1", sync.ReasonCodes);
        }

        [Fact]
        public void Scenario_SnapshotMismatch_IsFailClosedOnSyncFailure()
        {
            var provider = new FakeDataProvider
            {
                Snapshot = new DiagnosticSnapshot
                {
                    DomMarketData = new DomMarketDataObservation
                    {
                        HostAdapter = new DomCounterBlock
                        {
                            Observable = true,
                            SnapshotSyncCount = 10,
                            SnapshotSyncFailureCount = 3
                        }
                    }
                }
            };

            var results = new M5DomMarketDataProbe().Evaluate(Context(provider));
            Assert.Equal(DiagnosticStatus.FAIL, Find(results, "M5.SNAPSHOT_SYNC").Status);
        }

        // ---------------- M6: unknown direction, incomplete TradeDelta, OrderFlow unavailable ----------------

        [Fact]
        public void Scenario_UnknownTradeDirection_IsCountedWithCause()
        {
            var provider = new FakeDataProvider
            {
                Snapshot = new DiagnosticSnapshot
                {
                    TradeDelta = new TradeDeltaObservation
                    {
                        BuyDerivedCount = 1,
                        SellDerivedCount = 1,
                        UnknownCount = 5,
                        UnknownNoBboCount = 5
                    }
                }
            };

            var results = new M6TradeDeltaProbe().Evaluate(Context(provider));
            var unknown = Find(results, "M6.UNKNOWN_DIRECTION");
            Assert.Equal(DiagnosticStatus.INCONCLUSIVE, unknown.Status);
            Assert.Contains("NO_BBO=5", unknown.ReasonCodes);
        }

        [Fact]
        public void Scenario_IncompleteTradeDelta_IsInconclusiveNotPass()
        {
            var provider = new FakeDataProvider
            {
                Snapshot = new DiagnosticSnapshot
                {
                    TradeDelta = new TradeDeltaObservation
                    {
                        TradeDeltaCompleteness = "INCOMPLETE",
                        TradeDeltaUnavailableReason = "UNAVAILABLE_TRADE_DELTA"
                    }
                }
            };

            var results = new M6TradeDeltaProbe().Evaluate(Context(provider));
            var completeness = Find(results, "M6.TRADEDELTA_COMPLETENESS");
            Assert.Equal(DiagnosticStatus.INCONCLUSIVE, completeness.Status);
            Assert.Contains("UNAVAILABLE_TRADE_DELTA", completeness.ReasonCodes);
        }

        [Fact]
        public void Scenario_OrderFlowUnavailable_IsReportedWithScope()
        {
            var provider = new FakeDataProvider
            {
                Snapshot = new DiagnosticSnapshot
                {
                    TradeDelta = new TradeDeltaObservation
                    {
                        OrderFlowReadiness = "UNAVAILABLE",
                        OrderFlowUnavailableReason = "RESET_OPERATION_RECEIVED",
                        ResetObserved = true
                    }
                }
            };

            var results = new M6TradeDeltaProbe().Evaluate(Context(provider));
            var tension = Find(results, "M6.ORDERFLOW_READINESS_VS_STRATEGY_SKIP");
            Assert.Equal(DiagnosticStatus.INCONCLUSIVE, tension.Status);
            Assert.Contains("ORDERFLOW_READINESS=UNAVAILABLE", tension.ReasonCodes);
            Assert.Contains("NOT_DECLARED_CONTRADICTION", tension.ReasonCodes);
        }

        // ---------------- M7: missing strategy capability ----------------

        [Fact]
        public void Scenario_MissingStrategyCapability_IsReportedNotAssumedActive()
        {
            var observation = new StrategyRiskGateObservation { StrategySurfaceObservable = true };
            observation.Strategies.Add(new StrategyEvaluationObservation
            {
                StrategyId = "STRAT_ORDER_FLOW_INTRADAY_01",
                Available = false,
                MissingCapability = true,
                UnavailableReason = "ORDERFLOW_UNAVAILABLE"
            });

            var provider = new FakeDataProvider
            {
                Snapshot = new DiagnosticSnapshot { StrategyRiskGate = observation }
            };

            var results = new M7StrategyRiskGateProbe().Evaluate(Context(provider));
            var evaluation = Find(results, "M7.STRATEGY_EVALUATION");
            Assert.Equal(DiagnosticStatus.INCONCLUSIVE, evaluation.Status);
            Assert.Contains("MISSING_CAPABILITY=1", evaluation.ReasonCodes);
            Assert.Contains(evaluation.Evidence, e => e.ObservedValue.Contains("STRAT_ORDER_FLOW_INTRADAY_01"));
        }

        // ---------------- M9: report integrity + execution out of scope ----------------

        [Fact]
        public void Scenario_PostTradeAuditExecutionOutOfScope_IsInconclusive()
        {
            var provider = new FakeDataProvider
            {
                Snapshot = new DiagnosticSnapshot
                {
                    PostTradeAudit = new PostTradeAuditObservation
                    {
                        AuditSurfaceObservable = true,
                        ExecutionsInScope = false,
                        OrderIdClassCounts = new Dictionary<OrderIdClass, int> { { OrderIdClass.Valid, 1 } }
                    }
                }
            };

            var results = new M9PostTradeAuditProbe().Evaluate(Context(provider));
            Assert.Equal(DiagnosticStatus.INCONCLUSIVE, Find(results, "M9.EXECUTION_LIFECYCLE").Status);
            Assert.Equal(DiagnosticStatus.INCONCLUSIVE, Find(results, "M9.CORRELATION_TIMING").Status);
        }

        // ---------------- Data integrity: per-domain gaps surface through the pipeline ----------------

        [Fact]
        public void Scenario_MarketDataSequenceGap_FailsDomDataIntegrity()
        {
            var session = new SessionContext(SessionId.New(DateTime.UtcNow), DateTime.UtcNow);
            var pipeline = new EventIngestPipeline(64, 64, new FakeClock(DateTime.UtcNow));

            Assert.True(pipeline.Ingest(session, new MarketDataRecord { InstrumentId = "ES 12-26", Kind = MarketDataKind.Quote, SourceSequence = 1 }));
            Assert.True(pipeline.Ingest(session, new MarketDataRecord { InstrumentId = "ES 12-26", Kind = MarketDataKind.Quote, SourceSequence = 3 }));

            Assert.True(session.SequenceGapCount > 0);

            var merged = SnapshotAssembler.Assemble(new DiagnosticSnapshot(), session);
            var provider = new FakeDataProvider { Snapshot = merged };
            var results = new M5DomMarketDataProbe().Evaluate(Context(provider));
            Assert.Equal(DiagnosticStatus.FAIL, Find(results, "M5.DATA_INTEGRITY").Status);
        }

        [Fact]
        public void Scenario_TradeStreamSequenceGap_FailsOrderFlowContinuity()
        {
            var session = new SessionContext(SessionId.New(DateTime.UtcNow), DateTime.UtcNow);
            var pipeline = new EventIngestPipeline(64, 64, new FakeClock(DateTime.UtcNow));

            Assert.True(pipeline.Ingest(session, new MarketDataRecord { InstrumentId = "ES 12-26", Kind = MarketDataKind.Trade, SourceSequence = 10 }));
            Assert.True(pipeline.Ingest(session, new MarketDataRecord { InstrumentId = "ES 12-26", Kind = MarketDataKind.Trade, SourceSequence = 14 }));

            var merged = SnapshotAssembler.Assemble(new DiagnosticSnapshot(), session);
            var provider = new FakeDataProvider { Snapshot = merged };
            var results = new M6TradeDeltaProbe().Evaluate(Context(provider));
            Assert.Equal(DiagnosticStatus.FAIL, Find(results, "M6.SEQUENCE_CONTINUITY").Status);
        }

        // ---------------- Runtime: queue overflow + shutdown mid-capture ----------------

        [Fact]
        public void Scenario_QueueOverflow_IsReportedAndNeverSilentlyDropped()
        {
            var provider = new FakeDataProvider();
            using var runtime = Runtime(provider, 1);

            Assert.True(runtime.Ingest(new OrderRecord { OrderRef = "a", InstrumentId = "ES", State = OrderLifecycleState.Working }));
            Assert.False(runtime.Ingest(new OrderRecord { OrderRef = "b", InstrumentId = "ES", State = OrderLifecycleState.Working }));
            Assert.True(runtime.Session.DroppedCount > 0);

            var report = runtime.RunDiagnosticPass();
            var integrity = report.Results.FirstOrDefault(r => r.CheckId == "M0.EVENT_INTEGRITY");
            Assert.NotNull(integrity);
            Assert.Equal(DiagnosticStatus.FAIL, integrity.Status);
        }

        [Fact]
        public void Scenario_ShutdownMidCapture_DrainsQueuedRecords()
        {
            var provider = new FakeDataProvider();
            var runtime = Runtime(provider, 512);
            runtime.Start();

            for (var i = 0; i < 200; i++)
            {
                runtime.Ingest(new MarketDataRecord
                {
                    InstrumentId = "ES 12-26",
                    Kind = MarketDataKind.Quote,
                    SourceTimestampUtc = DateTime.UtcNow.AddTicks(i)
                });
            }

            // Dispose while records may still be in flight; the final drain must not abandon them.
            runtime.Dispose();

            Assert.Equal(200, runtime.Session.ProcessedCount);
            Assert.Equal(0, runtime.Session.DroppedCount);
        }

        [Fact]
        public void Scenario_TradingDisabled_IsReportedFromRuntimeEvidence()
        {
            var provider = new FakeDataProvider();
            using var runtime = Runtime(provider, 64);
            runtime.StaticSafetyScan = new SafetyScanEvidence { Passed = true, ForbiddenApiHits = 0, FilesScanned = 103 };
            runtime.Start();

            var report = runtime.RunDiagnosticPass();
            Assert.Equal(DiagnosticStatus.PASS, Find(report.Results, "M8.TRADING_DISABLED").Status);
            Assert.Equal(DiagnosticStatus.PASS, Find(report.Results, "M8.READ_ONLY_POSTURE").Status);
            Assert.Equal(DiagnosticStatus.PASS, Find(report.Results, "M8.STATIC_SAFETY_SCAN").Status);
        }

        // ---------------- Report integrity + redaction do not corrupt counters ----------------

        [Fact]
        public void Scenario_LargeCounterSurvivesRedactionAndSerialization()
        {
            var report = new DiagnosticReport
            {
                SessionId = SessionId.New(DateTime.UtcNow),
                GeneratedAtUtc = DateTime.UtcNow
            };
            report.Add(DiagnosticResult
                .Inconclusive("M7", "M7.CANDIDATE_REJECTION", "corr00000000", DateTime.UtcNow)
                .WithReasonCode("ALL_CANDIDATES_INVALID=311179"));

            var json = ReportSerializer.Serialize(report);
            Assert.Contains("ALL_CANDIDATES_INVALID=311179", json);
        }

        [Fact]
        public void Scenario_AccountLikeDigitRun_IsStillMasked()
        {
            Assert.Equal("acct " + SecretRedactor.Mask, SecretRedactor.Redact("acct 12345678"));
            Assert.Contains(SecretRedactor.Mask, SecretRedactor.Redact("account=12345678"));
            Assert.Contains(SecretRedactor.Mask, SecretRedactor.Redact("\"accountRef\":12345678"));
            // A structured counter value is preserved so evidence is not corrupted.
            Assert.Equal("ALL_CANDIDATES_INVALID=311179", SecretRedactor.Redact("ALL_CANDIDATES_INVALID=311179"));
            Assert.Equal("sync=265 failure=1", SecretRedactor.Redact("sync=265 failure=1"));
        }

        [Fact]
        public void Scenario_AdditionalCredentialKeywords_AreMasked()
        {
            Assert.Contains(SecretRedactor.Mask, SecretRedactor.Redact("passwd=s3cr3tvalue"));
            Assert.Contains(SecretRedactor.Mask, SecretRedactor.Redact("pwd = abc123"));
            Assert.Contains(SecretRedactor.Mask, SecretRedactor.Redact("otp=123456"));
        }

        [Fact]
        public void Scenario_DomIntegrity_IsInconclusiveWithoutObservedStream()
        {
            var provider = new FakeDataProvider
            {
                Snapshot = new DiagnosticSnapshot { DomMarketData = new DomMarketDataObservation() }
            };

            var results = new M5DomMarketDataProbe().Evaluate(Context(provider));
            Assert.Equal(DiagnosticStatus.INCONCLUSIVE, Find(results, "M5.DATA_INTEGRITY").Status);
        }

        [Fact]
        public void Scenario_DomIntegrity_FailsOnGapEvenWithoutObservedStream()
        {
            var provider = new FakeDataProvider
            {
                Snapshot = new DiagnosticSnapshot
                {
                    DomMarketData = new DomMarketDataObservation { SequenceGapCount = 3 }
                }
            };

            var results = new M5DomMarketDataProbe().Evaluate(Context(provider));
            Assert.Equal(DiagnosticStatus.FAIL, Find(results, "M5.DATA_INTEGRITY").Status);
        }

        [Fact]
        public void Scenario_DomIntegrity_PassesOnlyWithObservedCleanStream()
        {
            var provider = new FakeDataProvider
            {
                Snapshot = new DiagnosticSnapshot
                {
                    DomMarketData = new DomMarketDataObservation { Level1QuoteObserved = true }
                }
            };

            var results = new M5DomMarketDataProbe().Evaluate(Context(provider));
            Assert.Equal(DiagnosticStatus.PASS, Find(results, "M5.DATA_INTEGRITY").Status);
        }

        [Fact]
        public void Scenario_SequenceContinuity_IsInconclusiveWithoutObservedStream()
        {
            var provider = new FakeDataProvider
            {
                Snapshot = new DiagnosticSnapshot { TradeDelta = new TradeDeltaObservation() }
            };

            var results = new M6TradeDeltaProbe().Evaluate(Context(provider));
            Assert.Equal(DiagnosticStatus.INCONCLUSIVE, Find(results, "M6.SEQUENCE_CONTINUITY").Status);
        }

        [Fact]
        public void Scenario_SequenceContinuity_FailsOnGapEvenWithoutObservedStream()
        {
            var provider = new FakeDataProvider
            {
                Snapshot = new DiagnosticSnapshot
                {
                    TradeDelta = new TradeDeltaObservation { SequenceGapCount = 1 }
                }
            };

            var results = new M6TradeDeltaProbe().Evaluate(Context(provider));
            Assert.Equal(DiagnosticStatus.FAIL, Find(results, "M6.SEQUENCE_CONTINUITY").Status);
        }

        [Fact]
        public void Scenario_AuditMissingOrDuplicate_IsInconclusiveWithoutObservedSurface()
        {
            var provider = new FakeDataProvider
            {
                Snapshot = new DiagnosticSnapshot { PostTradeAudit = new PostTradeAuditObservation() }
            };

            var results = new M9PostTradeAuditProbe().Evaluate(Context(provider));
            Assert.Equal(DiagnosticStatus.INCONCLUSIVE, Find(results, "M9.MISSING_OR_DUPLICATE").Status);
        }

        [Fact]
        public void Scenario_AuditMissingOrDuplicate_PassesOnlyWithObservedCleanSurface()
        {
            var provider = new FakeDataProvider
            {
                Snapshot = new DiagnosticSnapshot
                {
                    PostTradeAudit = new PostTradeAuditObservation { AuditSurfaceObservable = true }
                }
            };

            var results = new M9PostTradeAuditProbe().Evaluate(Context(provider));
            Assert.Equal(DiagnosticStatus.PASS, Find(results, "M9.MISSING_OR_DUPLICATE").Status);
        }

        [Fact]
        public void Scenario_AuditMissingOrDuplicate_FailsOnMissingEvenWithoutObservedSurface()
        {
            var provider = new FakeDataProvider
            {
                Snapshot = new DiagnosticSnapshot
                {
                    PostTradeAudit = new PostTradeAuditObservation { UnmatchedTerminalOrderCount = 1 }
                }
            };

            var results = new M9PostTradeAuditProbe().Evaluate(Context(provider));
            Assert.Equal(DiagnosticStatus.FAIL, Find(results, "M9.MISSING_OR_DUPLICATE").Status);
        }

        [Fact]
        public void Scenario_EvidenceIntegrity_IsInconclusiveWithoutIngestedRecords()
        {
            var provider = new FakeDataProvider
            {
                Snapshot = new DiagnosticSnapshot { Safety = new SafetyObservation() }
            };
            var results = new M8SafetyAcceptanceProbe().Evaluate(Context(provider));
            Assert.Equal(DiagnosticStatus.INCONCLUSIVE, Find(results, "M8.EVIDENCE_INTEGRITY").Status);
        }

        [Fact]
        public void Scenario_EvidenceIntegrity_PassesOnlyAfterIngestingCleanRecords()
        {
            var provider = new FakeDataProvider
            {
                Snapshot = new DiagnosticSnapshot { Safety = new SafetyObservation() }
            };
            var session = new SessionContext(SessionId.New(DateTime.UtcNow), DateTime.UtcNow);
            session.RecordEnqueued();
            var context = new ProbeContext(provider, session, new FakeClock(DateTime.UtcNow), "corr00000000");

            var results = new M8SafetyAcceptanceProbe().Evaluate(context);
            Assert.Equal(DiagnosticStatus.PASS, Find(results, "M8.EVIDENCE_INTEGRITY").Status);
        }

        [Fact]
        public void Scenario_AccountAssociation_IsInconclusiveWhenNoAccountHasAConnection()
        {
            var provider = new FakeDataProvider();
            provider.Connections.Add(new ConnectionRecord
            {
                ProviderName = "Rithmic",
                PriceState = ConnectionState.Connected,
                OrderChannelState = ConnectionState.Connected,
                Role = ConnectionRole.Broker
            });
            provider.Snapshot = new DiagnosticSnapshot
            {
                TargetAccountConnection = new TargetAccountConnectionObservation
                {
                    AccountSurfaceObservable = true,
                    ObservedAccountCount = 1,
                    MatchingAccountCount = 1,
                    AccountProviderObservable = true,
                    AccountProviderMatches = true,
                    ConnectionObserved = false
                }
            };

            var results = new M2ConnectionProbe().Evaluate(Context(provider));
            Assert.Equal(DiagnosticStatus.INCONCLUSIVE, Find(results, "M2.ACCOUNT_CONNECTION_ASSOCIATION").Status);
        }

        [Fact]
        public void Scenario_AccountAssociation_PassesOnlyWithAnObservedTargetAccountBoundToTheRequiredConnection()
        {
            var provider = new FakeDataProvider();
            provider.Connections.Add(new ConnectionRecord
            {
                ProviderName = "Rithmic",
                PriceState = ConnectionState.Connected,
                OrderChannelState = ConnectionState.Connected,
                Role = ConnectionRole.Broker
            });
            provider.Snapshot = new DiagnosticSnapshot
            {
                TargetAccountConnection = new TargetAccountConnectionObservation
                {
                    AccountSurfaceObservable = true,
                    ObservedAccountCount = 1,
                    MatchingAccountCount = 1,
                    ConnectionCount = 1,
                    MatchingConnectionCount = 1,
                    AccountProviderObservable = true,
                    AccountProviderMatches = true,
                    ConnectionObserved = true,
                    ConnectionNameObservable = true,
                    ConnectionNameMatches = true,
                    ConnectionProviderObservable = true,
                    ConnectionProviderMatches = true
                }
            };

            var results = new M2ConnectionProbe().Evaluate(Context(provider));
            Assert.Equal(DiagnosticStatus.PASS, Find(results, "M2.ACCOUNT_CONNECTION_ASSOCIATION").Status);
        }

        [Fact]
        public void Scenario_EvidencePackage_VerifiesIntegrityEndToEnd()
        {
            var provider = new FakeDataProvider();
            using var runtime = Runtime(provider, 256);
            runtime.Start();
            runtime.Ingest(new OrderRecord
            {
                OrderRef = "ord-1",
                InstrumentId = "ES 12-26",
                State = OrderLifecycleState.Filled,
                Quantity = 1,
                FilledQuantity = 1
            });

            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (runtime.Session.ProcessedCount < 1 && DateTime.UtcNow < deadline)
            {
                Thread.Sleep(5);
            }

            var result = new Core.Run.DiagnosticRunResult
            {
                Mode = Core.Run.DiagnosticMode.Full,
                ModeId = "full",
                SessionId = runtime.Session.SessionId,
                FinalState = Core.Run.RunState.Completed
            };
            result.Report = runtime.RunDiagnosticPass();

            var sink = new InMemoryEvidenceSink();
            Assert.True(runtime.WriteEvidencePackage(result, sink, out var directory));
            Assert.False(string.IsNullOrEmpty(directory));
            Assert.Equal(17, sink.Files.Count);
        }

        [Fact]
        public void Scenario_RuntimeDataset_IsLabelledRuntimeProvenance()
        {
            var dataset = new EvidenceDataset();
            Assert.Equal(EvidenceProvenance.Runtime, dataset.Provenance);

            var package = EvidencePackageBuilder.Build(
                new Core.Run.DiagnosticRunResult { ModeId = "full" }, dataset);
            Assert.Equal(EvidenceProvenance.Runtime, package.Manifest.Provenance);
            Assert.Contains("\"evidenceProvenance\":\"RUNTIME\"", package.Read(EvidencePackageCatalog.ManifestJson));
        }

        [Fact]
        public void Scenario_SampleDataset_IsLabelledSampleAndBannerIsRendered()
        {
            var dataset = new EvidenceDataset { Provenance = EvidenceProvenance.Sample };
            var package = EvidencePackageBuilder.Build(
                new Core.Run.DiagnosticRunResult { ModeId = "full" }, dataset);

            Assert.Equal(EvidenceProvenance.Sample, package.Manifest.Provenance);
            Assert.Contains("\"evidenceProvenance\":\"SAMPLE\"", package.Read(EvidencePackageCatalog.ManifestJson));
            Assert.Contains("SAMPLE DATA", package.Read(EvidencePackageCatalog.SummaryHtml));
        }
    }
}
