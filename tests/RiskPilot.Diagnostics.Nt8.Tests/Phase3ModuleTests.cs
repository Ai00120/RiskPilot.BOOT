using System;
using System.Collections.Generic;
using System.Linq;
using RiskPilot.Diagnostics.Nt8.Core.Engine;
using RiskPilot.Diagnostics.Nt8.Core.Engine.Consumers;
using RiskPilot.Diagnostics.Nt8.Core.Engine.Probes;
using RiskPilot.Diagnostics.Nt8.Core.Models;
using RiskPilot.Diagnostics.Nt8.Core.Models.Observations;
using RiskPilot.Diagnostics.Nt8.Core.Result;
using RiskPilot.Diagnostics.Nt8.Core.Session;
using RiskPilot.Diagnostics.Nt8.Core.Status;
using RiskPilot.Diagnostics.Nt8.Host;
using RiskPilot.Diagnostics.Nt8.Tests.Fakes;
using Xunit;

namespace RiskPilot.Diagnostics.Nt8.Tests
{
    /// <summary>
    /// Phase 3 module tests. They drive the real production probes through the read-only
    /// <see cref="ProbeContext"/> and assert the fail-closed rules: missing evidence is
    /// INCONCLUSIVE, never PASS, and no finding is asserted without a positive observation.
    /// </summary>
    public class Phase3ModuleTests
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

        // ---------------- M1 ----------------

        [Fact]
        public void M1_Architecture_ReportsProcessBitness_WhenObservable()
        {
            var provider = new FakeDataProvider
            {
                Snapshot = new DiagnosticSnapshot
                {
                    Runtime = new RuntimeObservation { Is64BitProcess = true, ClrVersion = "4.0.30319.42000" }
                }
            };

            var results = new M1EnvironmentProbe().Evaluate(Context(provider));
            Assert.Equal(DiagnosticStatus.PASS, Find(results, "M1.PROCESS_ARCHITECTURE").Status);
            Assert.Equal(DiagnosticStatus.PASS, Find(results, "M1.CLR_RUNTIME").Status);
        }

        [Fact]
        public void M1_Architecture_IsInconclusive_WhenSnapshotAbsent()
        {
            var provider = new FakeDataProvider();
            var results = new M1EnvironmentProbe().Evaluate(Context(provider));
            Assert.Equal(DiagnosticStatus.INCONCLUSIVE, Find(results, "M1.PROCESS_ARCHITECTURE").Status);
        }

        [Fact]
        public void M1_CapabilityMatrix_Fails_WhenRequiredCapabilityUnavailable()
        {
            var runtime = new RuntimeObservation();
            runtime.Capabilities.Add(new RuntimeCapability("Account.Orders", CapabilityAvailability.Available, "ok"));
            runtime.Capabilities.Add(new RuntimeCapability("Instrument.MarketDepth", CapabilityAvailability.Unavailable, "missing"));
            runtime.Capabilities.Add(new RuntimeCapability("HostVersion", CapabilityAvailability.Unobservable, "no api"));
            var provider = new FakeDataProvider { Snapshot = new DiagnosticSnapshot { Runtime = runtime } };

            var results = new M1EnvironmentProbe().Evaluate(Context(provider));
            Assert.Equal(DiagnosticStatus.FAIL, Find(results, "M1.CAPABILITY_MATRIX").Status);
        }

        [Fact]
        public void M1_Lifecycle_RecordsObservedStates()
        {
            var runtime = new RuntimeObservation();
            runtime.LifecycleStates.Add("SetDefaults");
            runtime.LifecycleStates.Add("Active");
            var provider = new FakeDataProvider { Snapshot = new DiagnosticSnapshot { Runtime = runtime } };

            var results = new M1EnvironmentProbe().Evaluate(Context(provider));
            var lifecycle = Find(results, "M1.LIFECYCLE");
            Assert.Equal(DiagnosticStatus.PASS, lifecycle.Status);
            Assert.Contains("SetDefaults -> Active", lifecycle.Observation);
        }

        // ---------------- M2 ----------------

        [Fact]
        public void M2_ProviderFromStubValue_IsNeverPass()
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
                Provider = new ProviderEvidence
                {
                    RawProviderValue = "Rithmic",
                    ResolutionMethod = "String",
                    IsStubOrMockValue = true
                }
            };

            var results = new M2ConnectionProbe().Evaluate(Context(provider));
            var identity = Find(results, "M2.PROVIDER_IDENTITY");
            Assert.Equal(DiagnosticStatus.INCONCLUSIVE, identity.Status);
            Assert.Contains("PROVIDER_VALUE_FROM_STUB_OR_MOCK", identity.ReasonCodes);
        }

        [Fact]
        public void M2_ProviderRealRithmic_Passes()
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
                Provider = new ProviderEvidence { RawProviderValue = "Rithmic", ResolutionMethod = "String" }
            };

            var results = new M2ConnectionProbe().Evaluate(Context(provider));
            Assert.Equal(DiagnosticStatus.PASS, Find(results, "M2.PROVIDER_IDENTITY").Status);
        }

        [Fact]
        public void M2_AccountProviderMismatch_Fails()
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
                AccountAssociations = new List<AccountConnectionAssociation>
                {
                    new AccountConnectionAssociation
                    {
                        AccountRef = "acct-1",
                        AccountProvider = "Rithmic",
                        HasConnection = true,
                        ConnectionName = "Rithmic",
                        ConnectionProvider = "Simulator",
                        ProvidersAgree = false
                    }
                }
            };

            var results = new M2ConnectionProbe().Evaluate(Context(provider));
            Assert.Equal(DiagnosticStatus.FAIL, Find(results, "M2.ACCOUNT_CONNECTION_ASSOCIATION").Status);
        }

        // ---------------- M3 ----------------

        [Fact]
        public void M3_ClassifyOrderId_DistinguishesNullEmptyWhitespaceValid()
        {
            Assert.Equal(OrderIdClass.Null, M3OrderLifecycleConsumer.Classify(null));
            Assert.Equal(OrderIdClass.Empty, M3OrderLifecycleConsumer.Classify(string.Empty));
            Assert.Equal(OrderIdClass.Whitespace, M3OrderLifecycleConsumer.Classify("   "));
            Assert.Equal(OrderIdClass.Valid, M3OrderLifecycleConsumer.Classify("OID-1"));
        }

        [Fact]
        public void M3_MalformedOrderId_Fails()
        {
            var provider = new FakeDataProvider
            {
                Snapshot = new DiagnosticSnapshot
                {
                    OrderLifecycle = new OrderLifecycleObservation
                    {
                        OrderSurfaceObservable = true,
                        OrderSnapshotCount = 2,
                        OrderIdClassCounts = new Dictionary<OrderIdClass, int>
                        {
                            [OrderIdClass.Valid] = 1,
                            [OrderIdClass.Empty] = 1,
                            [OrderIdClass.Whitespace] = 0
                        }
                    }
                }
            };

            var results = new M3AccountOrderProbe().Evaluate(Context(provider));
            Assert.Equal(DiagnosticStatus.FAIL, Find(results, "M3.ORDERID_CLASSIFICATION").Status);
        }

        [Fact]
        public void M3_F05_IsInconclusive_WhenExecutionsOutOfScope()
        {
            var provider = new FakeDataProvider
            {
                Snapshot = new DiagnosticSnapshot
                {
                    OrderLifecycle = new OrderLifecycleObservation
                    {
                        OrderSurfaceObservable = true,
                        OrderSnapshotCount = 3,
                        ExecutionsInScope = false
                    }
                }
            };

            var results = new M3AccountOrderProbe().Evaluate(Context(provider));
            var f05 = Find(results, "M3.F05_ORDER_FILL_CONTINUITY");
            Assert.Equal(DiagnosticStatus.INCONCLUSIVE, f05.Status);
            Assert.Equal("F-05", f05.FindingId);
            Assert.Contains("EXECUTIONS_OUT_OF_SCOPE", f05.ReasonCodes);
        }

        [Fact]
        public void M3_F05_IsInconclusive_WhenUnmatchedEventsExist()
        {
            var provider = new FakeDataProvider
            {
                Snapshot = new DiagnosticSnapshot
                {
                    OrderLifecycle = new OrderLifecycleObservation
                    {
                        OrderSurfaceObservable = true,
                        OrderSnapshotCount = 3,
                        ExecutionsInScope = true,
                        UnmatchedTerminalOrderCount = 1
                    }
                }
            };

            var results = new M3AccountOrderProbe().Evaluate(Context(provider));
            Assert.Equal(DiagnosticStatus.INCONCLUSIVE, Find(results, "M3.F05_ORDER_FILL_CONTINUITY").Status);
        }

        [Fact]
        public void M3_F05_Passes_OnlyWhenEveryEventIsMatched()
        {
            var provider = new FakeDataProvider
            {
                Snapshot = new DiagnosticSnapshot
                {
                    OrderLifecycle = new OrderLifecycleObservation
                    {
                        OrderSurfaceObservable = true,
                        OrderSnapshotCount = 3,
                        ExecutionsInScope = true,
                        UnmatchedTerminalOrderCount = 0,
                        UnmatchedExecutionCount = 0
                    }
                }
            };

            var results = new M3AccountOrderProbe().Evaluate(Context(provider));
            Assert.Equal(DiagnosticStatus.PASS, Find(results, "M3.F05_ORDER_FILL_CONTINUITY").Status);
        }

        // ---------------- M4 ----------------

        [Fact]
        public void M4_HostUnavailable_ReportsHostUnavailableCause()
        {
            var provider = new FakeDataProvider
            {
                Snapshot = new DiagnosticSnapshot
                {
                    Attestation = new InstrumentAttestationObservation
                    {
                        HostAvailable = false,
                        FailureReasons = new List<string> { AttestationReasonCodes.HostUnavailable }
                    }
                }
            };

            var results = new M4InstrumentAttestationProbe().Evaluate(Context(provider));
            Assert.Equal(DiagnosticStatus.FAIL, Find(results, "M4.HOST_METADATA").Status);
            Assert.Contains(AttestationReasonCodes.HostUnavailable, Find(results, "M4.HOST_METADATA").ReasonCodes);
        }

        [Fact]
        public void M4_D078_IsFailClosedAndNeverAutoApproved()
        {
            var provider = new FakeDataProvider
            {
                Snapshot = new DiagnosticSnapshot
                {
                    Attestation = new InstrumentAttestationObservation
                    {
                        HostAvailable = true,
                        RegistryMatched = true,
                        D078Approved = false,
                        FailureReasons = new List<string> { AttestationReasonCodes.D078FieldApprovalNotApproved }
                    }
                }
            };

            var results = new M4InstrumentAttestationProbe().Evaluate(Context(provider));
            var d078 = Find(results, "M4.D078_APPROVAL");
            Assert.Equal(DiagnosticStatus.FAIL, d078.Status);
            Assert.Contains(AttestationReasonCodes.D078FieldApprovalNotApproved, d078.ReasonCodes);
        }

        [Fact]
        public void M4_MissingSpecification_IsReportedSeparately()
        {
            var provider = new FakeDataProvider
            {
                Snapshot = new DiagnosticSnapshot
                {
                    Attestation = new InstrumentAttestationObservation
                    {
                        HostAvailable = true,
                        RegistryMatched = false,
                        D078Approved = false,
                        FailureReasons = new List<string> { AttestationReasonCodes.MissingInstrumentSpecification }
                    }
                }
            };

            var results = new M4InstrumentAttestationProbe().Evaluate(Context(provider));
            Assert.Equal(DiagnosticStatus.FAIL, Find(results, "M4.REGISTRY_MATCH").Status);
            var outcome = Find(results, "M4.ATTESTATION_OUTCOME");
            Assert.Contains(AttestationReasonCodes.MissingInstrumentSpecification, outcome.ReasonCodes);
        }

        [Fact]
        public void M4_CanonicalInstrumentId_ParsesEvidenceContract()
        {
            Assert.Equal("ES_FUTURES_CME_DEC26", Core.Instruments.CanonicalInstrumentId.Build("ES 12-26", "CME"));
            Assert.Null(Core.Instruments.CanonicalInstrumentId.Build("not-a-contract", "CME"));
        }

        // ---------------- M5 ----------------

        [Fact]
        public void M5_ReportsTwoCounterLayersSeparately()
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
                            SnapshotSyncCount = 265,
                            SnapshotSyncFailureCount = 1,
                            UpdateOutOfBoundsCount = 264,
                            InsertOutOfBoundsCount = 1,
                            NativeSnapshotBids = 64,
                            NativeSnapshotAsks = 64,
                            InternalBidLevels = 10,
                            InternalAskLevels = 6
                        },
                        Recorder = new DomCounterBlock
                        {
                            Layer = "RECORDER",
                            Observable = true,
                            UpdateCount = 92988,
                            InsertCount = 7007,
                            RemoveCount = 2078,
                            ResetCount = 227
                        },
                        Level1QuoteObserved = true,
                        LastTradeObserved = true
                    }
                }
            };

            var results = new M5DomMarketDataProbe().Evaluate(Context(provider));
            var host = Find(results, "M5.HOST_LAYER_COUNTERS");
            var recorder = Find(results, "M5.RECORDER_LAYER_COUNTERS");
            Assert.Contains("LAYER=HOST_ADAPTER", host.ReasonCodes);
            Assert.Contains("LAYER=RECORDER", recorder.ReasonCodes);
            Assert.Equal(DiagnosticStatus.FAIL, Find(results, "M5.SNAPSHOT_SYNC").Status);
        }

        [Fact]
        public void M5_InvalidDomValues_Fail()
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
                            InsertCount = 1,
                            InvalidPriceCount = 3
                        }
                    }
                }
            };

            var results = new M5DomMarketDataProbe().Evaluate(Context(provider));
            Assert.Equal(DiagnosticStatus.FAIL, Find(results, "M5.VALIDITY").Status);
        }

        // ---------------- M6 ----------------

        [Fact]
        public void M6_QuoteDerivedDirections_AreNotReportedAsConfirmed()
        {
            var provider = new FakeDataProvider
            {
                Snapshot = new DiagnosticSnapshot
                {
                    TradeDelta = new TradeDeltaObservation
                    {
                        BuyDerivedCount = 12074,
                        SellDerivedCount = 12414,
                        UnknownCount = 96,
                        UnknownInsideSpreadCount = 96,
                        AllDirectionsQuoteDerived = true,
                        SourceTimeBasis = SourceTimeBasis.ServerTimeUnverified
                    }
                }
            };

            var results = new M6TradeDeltaProbe().Evaluate(Context(provider));
            var source = Find(results, "M6.DIRECTION_SOURCE");
            Assert.Contains("PROVENANCE=ALL_QUOTE_DERIVED", source.ReasonCodes);
            Assert.Equal(DiagnosticStatus.INCONCLUSIVE, source.Status);
        }

        [Fact]
        public void M6_UnknownDirections_AreCountedWithCauses()
        {
            var provider = new FakeDataProvider
            {
                Snapshot = new DiagnosticSnapshot
                {
                    TradeDelta = new TradeDeltaObservation
                    {
                        BuyDerivedCount = 1,
                        SellDerivedCount = 1,
                        UnknownCount = 96,
                        UnknownInsideSpreadCount = 96
                    }
                }
            };

            var results = new M6TradeDeltaProbe().Evaluate(Context(provider));
            var unknown = Find(results, "M6.UNKNOWN_DIRECTION");
            Assert.Contains("INSIDE_SPREAD=96", unknown.ReasonCodes);
        }

        [Fact]
        public void M6_ApparentTension_IsReportedAsTwoScopesNotAContradiction()
        {
            var provider = new FakeDataProvider
            {
                Snapshot = new DiagnosticSnapshot
                {
                    TradeDelta = new TradeDeltaObservation
                    {
                        OrderFlowReadiness = "AVAILABLE",
                        ResetObserved = true,
                        ResetCount = 1
                    }
                }
            };

            var results = new M6TradeDeltaProbe().Evaluate(Context(provider));
            var tension = Find(results, "M6.ORDERFLOW_READINESS_VS_STRATEGY_SKIP");
            Assert.Equal(DiagnosticStatus.INCONCLUSIVE, tension.Status);
            Assert.Contains("NOT_DECLARED_CONTRADICTION", tension.ReasonCodes);
            Assert.Contains("ORDERFLOW_READINESS=AVAILABLE", tension.ReasonCodes);
        }

        [Fact]
        public void M6_SequenceGap_Fails()
        {
            var provider = new FakeDataProvider
            {
                Snapshot = new DiagnosticSnapshot
                {
                    TradeDelta = new TradeDeltaObservation { SequenceGapCount = 2 }
                }
            };

            var results = new M6TradeDeltaProbe().Evaluate(Context(provider));
            Assert.Equal(DiagnosticStatus.FAIL, Find(results, "M6.SEQUENCE_CONTINUITY").Status);
        }

        // ---------------- M7 ----------------

        [Fact]
        public void M7_ReportsAllFiveRegisteredStrategies()
        {
            var observation = new StrategyRiskGateObservation { StrategySurfaceObservable = true };
            observation.Strategies.Add(new StrategyEvaluationObservation
            {
                StrategyId = "STRAT_SCALPING_CANDIDATE_01",
                Available = true,
                CandidateCount = 10,
                ValidCandidateCount = 2
            });

            var provider = new FakeDataProvider
            {
                Snapshot = new DiagnosticSnapshot { StrategyRiskGate = observation }
            };

            var results = new M7StrategyRiskGateProbe().Evaluate(Context(provider));
            var evaluation = Find(results, "M7.STRATEGY_EVALUATION");
            Assert.Contains("REGISTERED=5", evaluation.ReasonCodes);
            Assert.Equal(5, M7StrategyRiskGateProbe.RegisteredStrategies.Length);
        }

        [Fact]
        public void M7_UnavailableStrategy_IsReportedNotAssumedActive()
        {
            var observation = new StrategyRiskGateObservation { StrategySurfaceObservable = true };
            observation.Strategies.Add(new StrategyEvaluationObservation
            {
                StrategyId = "STRAT_ORDER_FLOW_INTRADAY_01",
                Available = false,
                UnavailableReason = "RESET_OPERATION_RECEIVED"
            });

            var provider = new FakeDataProvider
            {
                Snapshot = new DiagnosticSnapshot { StrategyRiskGate = observation }
            };

            var results = new M7StrategyRiskGateProbe().Evaluate(Context(provider));
            var evaluation = Find(results, "M7.STRATEGY_EVALUATION");
            Assert.Contains("UNAVAILABLE=1", evaluation.ReasonCodes);
        }

        [Fact]
        public void M7_StageCorrelation_IncompleteIsInconclusive()
        {
            var observation = new StrategyRiskGateObservation
            {
                StrategySurfaceObservable = true,
                Correlation = new StageCorrelationObservation
                {
                    DecisionId = "d1",
                    ProposalId = string.Empty,
                    RiskEvaluationId = "r1",
                    CorrelationComplete = false,
                    CorrelationGapReason = "PROPOSAL_ID_MISSING"
                }
            };

            var provider = new FakeDataProvider
            {
                Snapshot = new DiagnosticSnapshot { StrategyRiskGate = observation }
            };

            var results = new M7StrategyRiskGateProbe().Evaluate(Context(provider));
            var correlation = Find(results, "M7.STAGE_CORRELATION");
            Assert.Equal(DiagnosticStatus.INCONCLUSIVE, correlation.Status);
            Assert.Contains("PROPOSAL_ID_MISSING", correlation.ReasonCodes);
        }

        // ---------------- M8 ----------------

        [Fact]
        public void M8_TradingDisabled_IsInconclusiveWithoutRuntimeEvidence()
        {
            var provider = new FakeDataProvider
            {
                Snapshot = new DiagnosticSnapshot
                {
                    Safety = new SafetyObservation { TradingDisabledRuntimeEvidence = false }
                }
            };

            var results = new M8SafetyAcceptanceProbe().Evaluate(Context(provider));
            var trading = Find(results, "M8.TRADING_DISABLED");
            Assert.Equal(DiagnosticStatus.INCONCLUSIVE, trading.Status);
            Assert.Contains("TRADING_DISABLED_RUNTIME_EVIDENCE_MISSING", trading.ReasonCodes);
        }

        [Fact]
        public void M8_TradingDisabled_PassesWithRuntimeEvidence()
        {
            var provider = new FakeDataProvider
            {
                Snapshot = new DiagnosticSnapshot
                {
                    Safety = new SafetyObservation
                    {
                        TradingDisabledRuntimeEvidence = true,
                        ReadOnlyPostureObserved = true,
                        ReportSecretFree = true
                    }
                }
            };

            var results = new M8SafetyAcceptanceProbe().Evaluate(Context(provider));
            Assert.Equal(DiagnosticStatus.PASS, Find(results, "M8.TRADING_DISABLED").Status);
        }

        [Fact]
        public void M8_StaticScan_IsInconclusiveWhenNotSupplied()
        {
            var provider = new FakeDataProvider
            {
                Snapshot = new DiagnosticSnapshot { Safety = new SafetyObservation() }
            };

            var results = new M8SafetyAcceptanceProbe().Evaluate(Context(provider));
            Assert.Equal(DiagnosticStatus.INCONCLUSIVE, Find(results, "M8.STATIC_SAFETY_SCAN").Status);
        }

        [Fact]
        public void M8_UnobservableItems_AreAlwaysListed()
        {
            var provider = new FakeDataProvider
            {
                Snapshot = new DiagnosticSnapshot { Safety = new SafetyObservation() }
            };

            var results = new M8SafetyAcceptanceProbe().Evaluate(Context(provider));
            var unobservable = Find(results, "M8.UNOBSERVABLE");
            Assert.Equal(DiagnosticStatus.NOT_APPLICABLE, unobservable.Status);
            Assert.Contains(unobservable.Evidence, e => e.ObservedValue.Contains("NINJATRADER_RUNTIME_VERSION"));
        }

        // ---------------- Consumers ----------------

        [Fact]
        public void M6Consumer_CountsInsideSpreadUnknown()
        {
            var session = new SessionContext(SessionId.New(DateTime.UtcNow), DateTime.UtcNow);
            var consumer = new M6TradeDeltaConsumer(session);
            consumer.Consume(new MarketDataRecord
            {
                InstrumentId = "ES 12-26",
                Kind = MarketDataKind.Trade,
                TradeDirection = TradeDirection.Unknown,
                TradeDirectionSource = TradeDirectionSource.DerivedFromQuote,
                TradeDirectionUnknownReason = TradeDirectionUnknownReason.PriceInsideSpread
            });

            Assert.Equal(1, session.Counters.Get(RuntimeCounterKeys.TradeDirectionUnknown));
            Assert.Equal(1, session.Counters.Get(RuntimeCounterKeys.TradeDirectionInsideSpread));
            Assert.Equal(0, session.Counters.Get(RuntimeCounterKeys.TradeDirectionProviderSupplied));
        }

        [Fact]
        public void SnapshotAssembler_MergesCountersIntoObservation()
        {
            var session = new SessionContext(SessionId.New(DateTime.UtcNow), DateTime.UtcNow);
            session.Counters.Increment(RuntimeCounterKeys.OrderSnapshot, 3);
            session.Counters.Increment(RuntimeCounterKeys.OrderIdValid, 2);
            session.Counters.Increment(RuntimeCounterKeys.OrderIdEmpty, 1);

            var host = new DiagnosticSnapshot { OrderLifecycle = new OrderLifecycleObservation() };
            var merged = SnapshotAssembler.Assemble(host, session.Counters);

            Assert.Equal(3, merged.OrderLifecycle.OrderSnapshotCount);
            Assert.Equal(2, merged.OrderLifecycle.OrderIdClassCounts[OrderIdClass.Valid]);
            Assert.Equal(1, merged.OrderLifecycle.OrderIdClassCounts[OrderIdClass.Empty]);
        }

        [Fact]
        public void AllPhase3Probes_NeverPassWithoutEvidence()
        {
            // With an empty provider (no snapshot), no phase-3 check may be PASS.
            var provider = new FakeDataProvider();
            var context = Context(provider);
            var probes = new IDiagnosticProbe[]
            {
                new M3AccountOrderProbe(),
                new M4InstrumentAttestationProbe(),
                new M5DomMarketDataProbe(),
                new M6TradeDeltaProbe(),
                new M7StrategyRiskGateProbe(),
                new M8SafetyAcceptanceProbe()
            };

            foreach (var probe in probes)
            {
                foreach (var result in probe.Evaluate(context))
                {
                    Assert.NotEqual(DiagnosticStatus.PASS, result.Status);
                }
            }
        }
        // ---------------- Runtime integration ----------------

        [Fact]
        public void Runtime_DiagnosticPass_IncludesAllEightModules()
        {
            using var runtime = new DiagnosticRuntime(
                new FakeDataProvider(),
                new FakeClock(new DateTime(2026, 11, 12, 8, 0, 0, DateTimeKind.Utc)),
                64,
                64);
            runtime.Initialize();
            runtime.Start();

            var report = runtime.RunDiagnosticPass();
            var modules = new System.Collections.Generic.HashSet<string>();
            foreach (var result in report.Results)
            {
                modules.Add(result.ModuleId);
            }

            foreach (var module in new[] { "M1", "M2", "M3", "M4", "M5", "M6", "M7", "M8" })
            {
                Assert.Contains(module, modules);
            }
        }

        [Fact]
        public void Runtime_SuppliesTradingDisabledRuntimeEvidence_SoM8CanPass()
        {
            using var runtime = new DiagnosticRuntime(
                new FakeDataProvider(),
                new FakeClock(new DateTime(2026, 11, 12, 8, 0, 0, DateTimeKind.Utc)),
                64,
                64)
            {
                StaticSafetyScan = new SafetyScanEvidence { Passed = true, ForbiddenApiHits = 0, FilesScanned = 75 }
            };
            runtime.Initialize();
            runtime.Start();

            var report = runtime.RunDiagnosticPass();
            var trading = Find(report.Results, "M8.TRADING_DISABLED");
            Assert.Equal(DiagnosticStatus.PASS, trading.Status);
            Assert.Equal(DiagnosticStatus.PASS, Find(report.Results, "M8.STATIC_SAFETY_SCAN").Status);
        }

    }
}
