using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
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
    /// M6 (TradeDelta / OrderFlow) data-reliability tests. They drive the real production
    /// classifier, consumer, assembler, probe and host capture adapter and assert the
    /// truthfulness contract: a direction derived from a quote is never reported as
    /// provider-confirmed, a clean PASS requires the trade stream to have actually been
    /// observed (and its continuity measured), and a "COMPLETE" that the evidence contradicts
    /// fails rather than passing.
    /// </summary>
    public class M6TradeDeltaTests
    {
        private static readonly DateTime Now = new DateTime(2026, 11, 13, 12, 0, 0, DateTimeKind.Utc);

        private static ProbeContext Context(FakeDataProvider provider, DateTime now)
        {
            var session = new SessionContext(SessionId.New(now), now);
            return new ProbeContext(provider, session, new FakeClock(now), "corr00000000");
        }

        private static ProbeContext Context(FakeDataProvider provider)
        {
            return Context(provider, Now);
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

        private static FakeDataProvider ProviderWith(TradeDeltaObservation observation)
        {
            return new FakeDataProvider
            {
                Snapshot = new DiagnosticSnapshot { TradeDelta = observation }
            };
        }

        private static SessionContext Session()
        {
            return new SessionContext(SessionId.New(Now), Now);
        }

        // ---------------- TradeDirectionClassifier (pure) ----------------

        [Theory]
        [InlineData(5000.50, 5000.25, 5000.50, TradeDirection.Buy)]
        [InlineData(5000.00, 5000.00, 5000.25, TradeDirection.Sell)]
        [InlineData(5000.75, 5000.25, 5000.50, TradeDirection.Buy)]
        public void Classifier_PriceAtOrThroughQuote_Classifies(
            double price, double bid, double ask, TradeDirection expected)
        {
            TradeDirectionClassifier.Classify(price, bid, ask, out var direction, out var reason);

            Assert.Equal(expected, direction);
            Assert.Equal(TradeDirectionUnknownReason.NotApplicable, reason);
        }

        [Fact]
        public void Classifier_PriceInsideValidSpread_IsUnknownInsideSpread()
        {
            TradeDirectionClassifier.Classify(5000.375, 5000.25, 5000.50, out var direction, out var reason);

            Assert.Null(direction);
            Assert.Equal(TradeDirectionUnknownReason.PriceInsideSpread, reason);
        }

        [Fact]
        public void Classifier_CrossedBook_IsUnknownBboInvalid()
        {
            TradeDirectionClassifier.Classify(5000.375, 5000.75, 5000.50, out var direction, out var reason);

            Assert.Null(direction);
            Assert.Equal(TradeDirectionUnknownReason.BboInvalid, reason);
        }

        [Fact]
        public void Classifier_OneSidedQuote_IsUnknownBboIncomplete()
        {
            TradeDirectionClassifier.Classify(5000.375, 5000.25, null, out var direction, out var reason);

            Assert.Null(direction);
            Assert.Equal(TradeDirectionUnknownReason.BboIncomplete, reason);
        }

        [Fact]
        public void Classifier_NoQuote_IsUnknownNoBbo()
        {
            TradeDirectionClassifier.Classify(5000.375, null, null, out var direction, out var reason);

            Assert.Null(direction);
            Assert.Equal(TradeDirectionUnknownReason.NoBboAvailable, reason);
        }

        [Fact]
        public void Classifier_SentinelPrices_AreTreatedAsUnavailable()
        {
            Assert.Null(TradeDirectionClassifier.ToPositiveOrNull(0.0));
            Assert.Null(TradeDirectionClassifier.ToPositiveOrNull(double.NaN));
            Assert.Null(TradeDirectionClassifier.ToPositiveOrNull(double.PositiveInfinity));
            Assert.Equal(5000.25, TradeDirectionClassifier.ToPositiveOrNull(5000.25));
        }

        // ---------------- Consumer: real classification from the observed BBO ----------------

        [Fact]
        public void Consumer_ClassifiesBuyFromEventBbo_AndCountsAsDerived()
        {
            var session = Session();
            var consumer = new M6TradeDeltaConsumer(session);

            consumer.Consume(new MarketDataRecord
            {
                InstrumentId = "ES 12-26",
                Kind = MarketDataKind.Trade,
                Last = 5000.50,
                Bid = 5000.25,
                Ask = 5000.50,
                SourceTimeBasis = SourceTimeBasis.ServerTimeUnverified
            });

            Assert.Equal(1, session.Counters.Get(RuntimeCounterKeys.TradeDirectionBuy));
            Assert.Equal(1, session.Counters.Get(RuntimeCounterKeys.TradeStreamObserved));
            // A derived direction is never counted as provider-supplied.
            Assert.Equal(0, session.Counters.Get(RuntimeCounterKeys.TradeDirectionProviderSupplied));
        }

        [Fact]
        public void Consumer_FallsBackToLastObservedQuote_WhenTradeCarriesNoBbo()
        {
            var session = Session();
            var consumer = new M6TradeDeltaConsumer(session);

            consumer.Consume(new MarketDataRecord
            {
                InstrumentId = "ES 12-26",
                Kind = MarketDataKind.Quote,
                Bid = 5000.00,
                Ask = 5000.25
            });

            // No BBO on the print: the classifier falls back to the last observed quote.
            consumer.Consume(new MarketDataRecord
            {
                InstrumentId = "ES 12-26",
                Kind = MarketDataKind.Trade,
                Last = 5000.00
            });

            Assert.Equal(1, session.Counters.Get(RuntimeCounterKeys.TradeDirectionSell));
            Assert.Equal(0, session.Counters.Get(RuntimeCounterKeys.TradeDirectionUnknown));
        }

        [Fact]
        public void Consumer_UnknownWithNoBbo_IsCountedWithCause()
        {
            var session = Session();
            var consumer = new M6TradeDeltaConsumer(session);

            consumer.Consume(new MarketDataRecord
            {
                InstrumentId = "ES 12-26",
                Kind = MarketDataKind.Trade,
                Last = 5000.25
            });

            Assert.Equal(1, session.Counters.Get(RuntimeCounterKeys.TradeDirectionUnknown));
            Assert.Equal(1, session.Counters.Get(RuntimeCounterKeys.TradeDirectionNoBbo));
        }

        [Fact]
        public void Consumer_PreservesHostSuppliedClassification_AndDoesNotReDerive()
        {
            var session = Session();
            var consumer = new M6TradeDeltaConsumer(session);

            // The host already reported an unknown direction with a specific reason; the consumer
            // must count it exactly as reported, never override it with a quote derivation.
            consumer.Consume(new MarketDataRecord
            {
                InstrumentId = "ES 12-26",
                Kind = MarketDataKind.Trade,
                Last = 5000.50,
                Bid = 5000.25,
                Ask = 5000.50,
                TradeDirection = TradeDirection.Unknown,
                TradeDirectionSource = TradeDirectionSource.Unspecified,
                TradeDirectionUnknownReason = TradeDirectionUnknownReason.ProviderReportedUnknown
            });

            Assert.Equal(1, session.Counters.Get(RuntimeCounterKeys.TradeDirectionUnknown));
            Assert.Equal(1, session.Counters.Get(RuntimeCounterKeys.TradeDirectionProviderUnknown));
            Assert.Equal(0, session.Counters.Get(RuntimeCounterKeys.TradeDirectionBuy));
        }

        [Fact]
        public void Consumer_InvalidPrintPrice_IsNotMisClassifiedFromQuote()
        {
            var session = Session();
            var consumer = new M6TradeDeltaConsumer(session);

            // A print with a zero/non-finite price must never be compared to the BBO: 0 <= bid
            // would otherwise produce a confident, wrong Sell. It stays unknown (no cause).
            consumer.Consume(new MarketDataRecord
            {
                InstrumentId = "ES 12-26",
                Kind = MarketDataKind.Trade,
                Last = 0.0,
                Bid = 5000.25,
                Ask = 5000.50
            });
            consumer.Consume(new MarketDataRecord
            {
                InstrumentId = "ES 12-26",
                Kind = MarketDataKind.Trade,
                Last = double.NaN,
                Bid = 5000.25,
                Ask = 5000.50
            });

            Assert.Equal(0, session.Counters.Get(RuntimeCounterKeys.TradeDirectionBuy));
            Assert.Equal(0, session.Counters.Get(RuntimeCounterKeys.TradeDirectionSell));
            Assert.Equal(2, session.Counters.Get(RuntimeCounterKeys.TradeDirectionUnknown));
            Assert.Equal(2, session.Counters.Get(RuntimeCounterKeys.TradeDirectionUnclassified));
        }

        [Fact]
        public void Consumer_CountsSequenceObservation_AndWeakestSourceTimeBasis()
        {
            var session = Session();
            var consumer = new M6TradeDeltaConsumer(session);

            consumer.Consume(new MarketDataRecord
            {
                InstrumentId = "ES 12-26",
                Kind = MarketDataKind.Trade,
                Last = 5000.50,
                Bid = 5000.25,
                Ask = 5000.50,
                SourceSequence = 10,
                SourceTimeBasis = SourceTimeBasis.ExchangeTimeVerified
            });

            consumer.Consume(new MarketDataRecord
            {
                InstrumentId = "ES 12-26",
                Kind = MarketDataKind.Trade,
                Last = 5000.50,
                Bid = 5000.25,
                Ask = 5000.50,
                SourceSequence = 11,
                SourceTimeBasis = SourceTimeBasis.ServerTimeUnverified
            });

            Assert.Equal(2, session.Counters.Get(RuntimeCounterKeys.TradeDeltaSequenceObserved));

            // The worst (most conservative) basis wins: ServerTimeUnverified (1) encoded as 2.
            Assert.Equal((long)SourceTimeBasis.ServerTimeUnverified + 1,
                session.Counters.Get(RuntimeCounterKeys.TradeDeltaSourceTimeBasisWorst));
        }

        [Fact]
        public void Consumer_CountsMarketDataAndDepthResets_Separately()
        {
            var session = Session();
            var consumer = new M6TradeDeltaConsumer(session);

            consumer.Consume(new MarketDataRecord { InstrumentId = "ES 12-26", Kind = MarketDataKind.Quote, IsReset = true });
            consumer.Consume(new DomRecord { InstrumentId = "ES 12-26", Operation = DomOperation.Reset });

            Assert.Equal(1, session.Counters.Get(RuntimeCounterKeys.TradeDeltaResetMarketData));
            Assert.Equal(1, session.Counters.Get(RuntimeCounterKeys.TradeDeltaResetDepth));
        }

        // ---------------- SnapshotAssembler derivation ----------------

        [Fact]
        public void Assembler_DerivesObservability_Completeness_ResetsAndBasis()
        {
            var session = Session();
            var consumer = new M6TradeDeltaConsumer(session);
            consumer.Consume(new MarketDataRecord
            {
                InstrumentId = "ES 12-26",
                Kind = MarketDataKind.Trade,
                Last = 5000.50,
                Bid = 5000.25,
                Ask = 5000.50,
                SourceSequence = 5,
                SourceTimeBasis = SourceTimeBasis.ServerTimeUnverified
            });
            consumer.Consume(new DomRecord { InstrumentId = "ES 12-26", Operation = DomOperation.Reset });

            var merged = SnapshotAssembler.Assemble(new DiagnosticSnapshot(), session);
            var td = merged.TradeDelta;

            Assert.True(td.TradeStreamObservable);
            Assert.True(td.SequenceObservable);
            Assert.True(td.AllDirectionsQuoteDerived);
            Assert.Equal(1, td.BuyDerivedCount);
            Assert.Equal(SourceTimeBasis.ServerTimeUnverified, td.SourceTimeBasis);
            Assert.True(td.ResetObserved);
            Assert.Equal(1, td.DepthResetCount);
            Assert.Equal("COMPLETE", td.TradeDeltaCompleteness);
        }

        [Fact]
        public void Assembler_UnclassifiedDirection_MakesCompletenessIncomplete()
        {
            var session = Session();
            // A print with no classification and no reason (no BBO at all is still a reason, so
            // simulate a host-supplied unknown with no reason) is counted as unclassified.
            session.Counters.Increment(RuntimeCounterKeys.TradeStreamObserved);
            session.Counters.Increment(RuntimeCounterKeys.TradeDirectionUnknown);
            session.Counters.Increment(RuntimeCounterKeys.TradeDirectionUnclassified);

            var merged = SnapshotAssembler.Assemble(new DiagnosticSnapshot(), session);

            Assert.Equal("INCOMPLETE", merged.TradeDelta.TradeDeltaCompleteness);
            Assert.Equal("UNCLASSIFIED_TRADE_DIRECTION", merged.TradeDelta.TradeDeltaUnavailableReason);
        }

        [Fact]
        public void Assembler_NoObservedStream_CompletenessStaysUnobservable()
        {
            var merged = SnapshotAssembler.Assemble(new DiagnosticSnapshot(), Session());

            Assert.False(merged.TradeDelta.TradeStreamObservable);
            Assert.Equal("UNOBSERVABLE", merged.TradeDelta.TradeDeltaCompleteness);
            // With no observed trade record the source-time basis is never fabricated.
            Assert.Equal(SourceTimeBasis.Unknown, merged.TradeDelta.SourceTimeBasis);
        }

        // ---------------- Probe: trade-stream surface ----------------

        [Fact]
        public void Probe_NoObservedTradeStream_NeverPassesAndReportsSurfaceUnobservable()
        {
            var results = new M6TradeDeltaProbe().Evaluate(Context(ProviderWith(new TradeDeltaObservation())));

            Assert.DoesNotContain(results, r => r.Status == DiagnosticStatus.PASS);
            Assert.Equal(DiagnosticStatus.INCONCLUSIVE, Find(results, "M6.TRADE_STREAM").Status);
        }

        [Fact]
        public void Probe_NullTradeDeltaObservation_ReportsEveryCheckAndNeverPasses()
        {
            var provider = new FakeDataProvider { Snapshot = new DiagnosticSnapshot { TradeDelta = null } };

            var results = new M6TradeDeltaProbe().Evaluate(Context(provider));

            Assert.DoesNotContain(results, r => r.Status == DiagnosticStatus.PASS);
            Assert.Equal(DiagnosticStatus.INCONCLUSIVE, Find(results, "M6.TRADE_STREAM").Status);
            Assert.Equal(DiagnosticStatus.INCONCLUSIVE, Find(results, "M6.SEQUENCE_CONTINUITY").Status);
            Assert.Equal(DiagnosticStatus.INCONCLUSIVE, Find(results, "M6.TRADEDELTA_COMPLETENESS").Status);
            Assert.Equal(DiagnosticStatus.INCONCLUSIVE, Find(results, "M6.DIRECTION_SOURCE").Status);
        }

        [Fact]
        public void Probe_ObservedTradeStream_PassesSurfaceCheck()
        {
            var provider = ProviderWith(new TradeDeltaObservation
            {
                TradeStreamObservable = true,
                BuyDerivedCount = 3,
                SellDerivedCount = 2
            });

            var results = new M6TradeDeltaProbe().Evaluate(Context(provider));
            Assert.Equal(DiagnosticStatus.PASS, Find(results, "M6.TRADE_STREAM").Status);
        }

        // ---------------- Probe: sequence continuity (fail-closed) ----------------

        [Fact]
        public void Probe_SequenceContinuity_IsInconclusiveWithoutObservedStream()
        {
            var results = new M6TradeDeltaProbe().Evaluate(Context(ProviderWith(new TradeDeltaObservation())));

            Assert.Equal(DiagnosticStatus.INCONCLUSIVE, Find(results, "M6.SEQUENCE_CONTINUITY").Status);
        }

        [Fact]
        public void Probe_SequenceContinuity_IsInconclusiveWhenNoSourceSequenceObserved()
        {
            // A trade stream was observed but the host exposed no source sequence, so continuity
            // was never measurable: a zero gap count must not be reported as a clean PASS.
            var provider = ProviderWith(new TradeDeltaObservation
            {
                TradeStreamObservable = true,
                SequenceObservable = false,
                BuyDerivedCount = 10,
                SellDerivedCount = 9
            });

            var results = new M6TradeDeltaProbe().Evaluate(Context(provider));
            Assert.Equal(DiagnosticStatus.INCONCLUSIVE, Find(results, "M6.SEQUENCE_CONTINUITY").Status);
        }

        [Fact]
        public void Probe_SequenceContinuity_PassesOnlyWithObservedCleanSequence()
        {
            var provider = ProviderWith(new TradeDeltaObservation
            {
                TradeStreamObservable = true,
                SequenceObservable = true,
                SequenceGapCount = 0,
                BuyDerivedCount = 10,
                SellDerivedCount = 9
            });

            var results = new M6TradeDeltaProbe().Evaluate(Context(provider));
            Assert.Equal(DiagnosticStatus.PASS, Find(results, "M6.SEQUENCE_CONTINUITY").Status);
        }

        [Fact]
        public void Probe_SequenceContinuity_FailsOnGapEvenWithoutObservedStream()
        {
            var provider = ProviderWith(new TradeDeltaObservation { SequenceGapCount = 1 });

            var results = new M6TradeDeltaProbe().Evaluate(Context(provider));
            Assert.Equal(DiagnosticStatus.FAIL, Find(results, "M6.SEQUENCE_CONTINUITY").Status);
        }

        // ---------------- Probe: TradeDelta completeness (corroborated) ----------------

        [Fact]
        public void Probe_Completeness_CompleteWithoutObservedStream_IsContradictedFail()
        {
            var provider = ProviderWith(new TradeDeltaObservation { TradeDeltaCompleteness = "COMPLETE" });

            var results = new M6TradeDeltaProbe().Evaluate(Context(provider));
            var completeness = Find(results, "M6.TRADEDELTA_COMPLETENESS");
            Assert.Equal(DiagnosticStatus.FAIL, completeness.Status);
            Assert.Contains("COMPLETENESS_CONTRADICTED_NO_TRADE_STREAM", completeness.ReasonCodes);
        }

        [Fact]
        public void Probe_Completeness_CompleteWithSequenceGap_IsContradictedFail()
        {
            var provider = ProviderWith(new TradeDeltaObservation
            {
                TradeDeltaCompleteness = "COMPLETE",
                TradeStreamObservable = true,
                SequenceObservable = true,
                SequenceGapCount = 2
            });

            var results = new M6TradeDeltaProbe().Evaluate(Context(provider));
            var completeness = Find(results, "M6.TRADEDELTA_COMPLETENESS");
            Assert.Equal(DiagnosticStatus.FAIL, completeness.Status);
            Assert.Contains("COMPLETENESS_CONTRADICTED_SEQUENCE_GAP", completeness.ReasonCodes);
        }

        [Fact]
        public void Probe_Completeness_CorroboratedComplete_Passes()
        {
            var provider = ProviderWith(new TradeDeltaObservation
            {
                TradeDeltaCompleteness = "COMPLETE",
                TradeStreamObservable = true,
                SequenceObservable = true,
                SequenceGapCount = 0,
                BuyDerivedCount = 5,
                SellDerivedCount = 4
            });

            var results = new M6TradeDeltaProbe().Evaluate(Context(provider));
            Assert.Equal(DiagnosticStatus.PASS, Find(results, "M6.TRADEDELTA_COMPLETENESS").Status);
        }

        [Fact]
        public void Probe_Completeness_Incomplete_IsInconclusiveNotPass()
        {
            var provider = ProviderWith(new TradeDeltaObservation
            {
                TradeDeltaCompleteness = "INCOMPLETE",
                TradeDeltaUnavailableReason = "UNAVAILABLE_TRADE_DELTA"
            });

            var results = new M6TradeDeltaProbe().Evaluate(Context(provider));
            var completeness = Find(results, "M6.TRADEDELTA_COMPLETENESS");
            Assert.Equal(DiagnosticStatus.INCONCLUSIVE, completeness.Status);
            Assert.Contains("UNAVAILABLE_TRADE_DELTA", completeness.ReasonCodes);
        }

        // ---------------- Probe: readiness is unobservable without host data ----------------

        [Fact]
        public void Probe_OrderFlowReadiness_UnobservableWhenNotSupplied()
        {
            var provider = ProviderWith(new TradeDeltaObservation { ResetObserved = true, ResetCount = 1 });

            var results = new M6TradeDeltaProbe().Evaluate(Context(provider));
            var tension = Find(results, "M6.ORDERFLOW_READINESS_VS_STRATEGY_SKIP");
            Assert.Equal(DiagnosticStatus.INCONCLUSIVE, tension.Status);
            Assert.Contains("ORDERFLOW_READINESS=UNOBSERVABLE", tension.ReasonCodes);
            Assert.Contains(tension.Evidence, e => !e.IsObservable);
        }

        // ---------------- Probe: direction provenance ----------------

        [Fact]
        public void Probe_QuoteDerivedDirections_AreNotReportedAsConfirmed()
        {
            var provider = ProviderWith(new TradeDeltaObservation
            {
                TradeStreamObservable = true,
                BuyDerivedCount = 12074,
                SellDerivedCount = 12414,
                UnknownCount = 96,
                UnknownInsideSpreadCount = 96,
                AllDirectionsQuoteDerived = true,
                SourceTimeBasis = SourceTimeBasis.ServerTimeUnverified
            });

            var results = new M6TradeDeltaProbe().Evaluate(Context(provider));
            var source = Find(results, "M6.DIRECTION_SOURCE");
            Assert.Contains("PROVENANCE=ALL_QUOTE_DERIVED", source.ReasonCodes);
            Assert.Equal(DiagnosticStatus.INCONCLUSIVE, source.Status);
        }

        // ---------------- Host capture: Level I trade classification over the stub surface ----------------

        [Collection("StubHostState")]
        public class HostCapture
        {
            private static void Reset()
            {
                Instrument.All.Clear();
                Account.All.Clear();
                Connection.Connections.Clear();
            }

            private static Instrument EsInstrument()
            {
                return new Instrument
                {
                    FullName = "ES 12-26",
                    Exchange = Exchange.Globex,
                    MasterInstrument = new MasterInstrument { Name = "ES" }
                };
            }

            [Fact]
            public void Capture_Level1TradeWithBbo_IsClassifiedByTheConsumer()
            {
                Reset();
                try
                {
                    var instrument = EsInstrument();
                    Instrument.All.Add(instrument);

                    var session = Session();
                    var consumer = new M6TradeDeltaConsumer(session);
                    var capture = new MarketDataCapture(r => { consumer.Consume(r); return true; }, () => true);
                    Assert.True(capture.TrySubscribe());

                    // The host reports a print at the ask with the BBO attached to the same event.
                    instrument.MarketData.RaiseUpdate(new MarketDataEventArgs
                    {
                        Instrument = instrument,
                        MarketDataType = MarketDataType.Last,
                        Price = 5000.50,
                        Bid = 5000.25,
                        Ask = 5000.50,
                        Volume = 4,
                        Time = Now
                    });

                    Assert.Equal(1, session.Counters.Get(RuntimeCounterKeys.TradeStreamObserved));
                    Assert.Equal(1, session.Counters.Get(RuntimeCounterKeys.TradeDirectionBuy));

                    capture.Detach();
                }
                finally
                {
                    Reset();
                }
            }

            [Fact]
            public void Capture_DepthReset_IsCountedForOrderFlow()
            {
                Reset();
                try
                {
                    var instrument = EsInstrument();
                    Instrument.All.Add(instrument);

                    var session = Session();
                    var consumer = new M6TradeDeltaConsumer(session);
                    var capture = new MarketDataCapture(r => { consumer.Consume(r); return true; }, () => true);
                    Assert.True(capture.TrySubscribe());

                    instrument.MarketDepth.RaiseUpdate(new MarketDepthEventArgs
                    {
                        Instrument = instrument,
                        Operation = Operation.Update,
                        Position = 0,
                        Price = 5000.00,
                        Volume = 3,
                        IsReset = true,
                        Time = Now
                    });

                    Assert.Equal(1, session.Counters.Get(RuntimeCounterKeys.TradeDeltaResetDepth));

                    capture.Detach();
                }
                finally
                {
                    Reset();
                }
            }
        }

        // ---------------- Runtime integration: observed trades reach the exported M6 evidence ----------------

        [Fact]
        public void Runtime_ObservedTrades_ReachExportedTradeDeltaEvidence()
        {
            var provider = new FakeDataProvider { Snapshot = new DiagnosticSnapshot() };
            var runtime = new DiagnosticRuntime(provider, new FakeClock(Now), 512, 64, 512);
            runtime.Initialize();
            runtime.Start();

            Assert.True(runtime.Ingest(new MarketDataRecord
            {
                InstrumentId = "ES 12-26",
                Kind = MarketDataKind.Trade,
                Last = 5000.50,
                Bid = 5000.25,
                Ask = 5000.50,
                SourceSequence = 1,
                SourceTimeBasis = SourceTimeBasis.ServerTimeUnverified
            }));
            Assert.True(runtime.Ingest(new MarketDataRecord
            {
                InstrumentId = "ES 12-26",
                Kind = MarketDataKind.Trade,
                Last = 5000.00,
                Bid = 5000.00,
                Ask = 5000.25,
                SourceSequence = 2,
                SourceTimeBasis = SourceTimeBasis.ServerTimeUnverified
            }));

            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (runtime.Session.ProcessedCount < 2 && DateTime.UtcNow < deadline)
            {
                Thread.Sleep(5);
            }

            var dataset = runtime.BuildEvidenceDataset();
            var td = dataset.Snapshot.TradeDelta;

            Assert.True(td.TradeStreamObservable);
            Assert.True(td.SequenceObservable);
            Assert.Equal(1, td.BuyDerivedCount);
            Assert.Equal(1, td.SellDerivedCount);
            Assert.Equal(0, td.UnknownCount);
            Assert.Equal(SourceTimeBasis.ServerTimeUnverified, td.SourceTimeBasis);

            runtime.Dispose();
        }
    }
}
