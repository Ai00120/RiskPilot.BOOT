using System;
using System.Collections.Generic;
using NinjaTrader.Cbi;
using RiskPilot.Diagnostics.Nt8.Core.Engine;
using RiskPilot.Diagnostics.Nt8.Core.Engine.Probes;
using RiskPilot.Diagnostics.Nt8.Core.Evidence;
using RiskPilot.Diagnostics.Nt8.Core.Identity;
using RiskPilot.Diagnostics.Nt8.Core.Models.Observations;
using RiskPilot.Diagnostics.Nt8.Core.Report;
using RiskPilot.Diagnostics.Nt8.Core.Result;
using RiskPilot.Diagnostics.Nt8.Core.Run;
using RiskPilot.Diagnostics.Nt8.Core.Session;
using RiskPilot.Diagnostics.Nt8.Core.Status;
using RiskPilot.Diagnostics.Nt8.Host;
using RiskPilot.Diagnostics.Nt8.Tests.Fakes;
using Xunit;

namespace RiskPilot.Diagnostics.Nt8.Tests
{
    /// <summary>
    /// Regression tests for the M4 instrument-metadata read-status fix. A property getter that
    /// throws must remain observable (recorded per field), one failed field must not erase another
    /// field's result, and a required-field read failure must never produce
    /// <c>M4.FIELD_METADATA = PASS</c>.
    /// <para>
    /// The host-adapter cases run over the OFFLINE NinjaTrader stub surface; a green stub run is
    /// NOT proof of real host behaviour.
    /// </para>
    /// </summary>
    [Collection("StubHostState")]
    public class M4FieldReadStatusTests
    {
        private static void Reset()
        {
            Instrument.All.Clear();
            Account.All.Clear();
            Connection.Connections.Clear();
        }

        private static Connection ConnectedRithmic()
        {
            return new Connection
            {
                Status = ConnectionStatus.Connected,
                PriceStatus = ConnectionStatus.Connected,
                Options = new ConnectOptions
                {
                    Name = "My Rithmic",
                    Provider = Provider.Rithmic,
                    Mode = Mode.Simulation
                }
            };
        }

        private static Instrument EsInstrument()
        {
            return new Instrument
            {
                FullName = "ES 12-26",
                Exchange = Exchange.Globex,
                MasterInstrument = new MasterInstrument
                {
                    Name = "ES",
                    InstrumentType = InstrumentType.Future,
                    Currency = Currency.UsDollar,
                    TickSize = 0.25,
                    PointValue = 50,
                    Exchanges = new List<Exchange> { Exchange.Globex }
                }
            };
        }

        private static ProbeContext Context(IDiagnosticDataProvider provider)
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

        private static ProbeContext AttestationContext(InstrumentMetadataObservation metadata, bool hostAvailable = true)
        {
            var provider = new FakeDataProvider
            {
                Snapshot = new DiagnosticSnapshot
                {
                    Attestation = new InstrumentAttestationObservation
                    {
                        HostAvailable = hostAvailable,
                        Metadata = metadata,
                        FailureReasons = new List<string>()
                    }
                }
            };
            return Context(provider);
        }

        /// <summary>A metadata observation where every required field was successfully read.</summary>
        private static InstrumentMetadataObservation FullyReadMetadata()
        {
            return new InstrumentMetadataObservation
            {
                FullName = "ES 12-26",
                FullNameObservable = true,
                ExpiryRaw = "2026-12-18",
                ExpiryObservable = true,
                ExpiryKind = "Unspecified",
                ExchangeRaw = "Globex",
                ExchangeObservable = true,
                RootName = "ES",
                RootNameObservable = true,
                InstrumentTypeRaw = "Future",
                InstrumentTypeObservable = true,
                CurrencyRaw = "UsDollar",
                CurrencyObservable = true,
                PointValue = 50d,
                TickSize = 0.25d,
                TickValue = 12.5d,
                MasterMetadataAvailable = true
            };
        }

        // ---------------- host adapter: per-field read status ----------------

        [Fact]
        public void Attestation_ThrowingGetter_IsRecordedAsFieldReadFailure()
        {
            Reset();
            try
            {
                Connection.Connections.Add(ConnectedRithmic());
                var instrument = EsInstrument();
                instrument.MasterInstrument.ThrowOnNameRead = true;
                Instrument.All.Add(instrument);

                // A configured id lets the target resolve without reading the throwing root field.
                var provider = new Nt8HostDataProvider(masterMetadataReady: true, requestedInstrumentId: "ES 12-26");
                var metadata = provider.GetSnapshot().Attestation.Metadata;

                Assert.NotNull(metadata);
                Assert.True(metadata.HasReadFailures);
                Assert.Contains("RootName", metadata.ReadFailures);
                Assert.False(metadata.RootNameObservable);
            }
            finally
            {
                Reset();
            }
        }

        [Fact]
        public void Attestation_OneFailedField_DoesNotEraseOtherFields()
        {
            Reset();
            try
            {
                Connection.Connections.Add(ConnectedRithmic());
                var instrument = EsInstrument();
                instrument.MasterInstrument.ThrowOnTickSizeRead = true;
                Instrument.All.Add(instrument);

                var provider = new Nt8HostDataProvider(masterMetadataReady: true);
                var metadata = provider.GetSnapshot().Attestation.Metadata;

                // The failed field is recorded; the other fields remain successfully read.
                Assert.Single(metadata.ReadFailures);
                Assert.Contains("TickSize", metadata.ReadFailures);
                Assert.Null(metadata.TickSize);
                Assert.Equal(50d, metadata.PointValue);
                Assert.True(metadata.RootNameObservable);
                Assert.Equal("ES", metadata.RootName);
                Assert.True(metadata.CurrencyObservable);
                Assert.True(metadata.FullNameObservable);
                Assert.True(metadata.ExpiryObservable);
                Assert.True(metadata.ExchangeObservable);
            }
            finally
            {
                Reset();
            }
        }

        [Fact]
        public void Attestation_FullyReadMetadata_HasNoReadFailures()
        {
            Reset();
            try
            {
                Connection.Connections.Add(ConnectedRithmic());
                Instrument.All.Add(EsInstrument());

                var provider = new Nt8HostDataProvider(masterMetadataReady: true);
                var metadata = provider.GetSnapshot().Attestation.Metadata;

                Assert.False(metadata.HasReadFailures);
                Assert.True(metadata.FullNameObservable);
                Assert.True(metadata.ExpiryObservable);
                Assert.True(metadata.ExchangeObservable);
                Assert.True(metadata.RootNameObservable);
                Assert.True(metadata.InstrumentTypeObservable);
                Assert.True(metadata.CurrencyObservable);
                Assert.True(metadata.PointValue.HasValue);
                Assert.True(metadata.TickSize.HasValue);
            }
            finally
            {
                Reset();
            }
        }

        [Fact]
        public void Attestation_ThrowingGetter_FieldMetadataCannotPass()
        {
            Reset();
            try
            {
                Connection.Connections.Add(ConnectedRithmic());
                var instrument = EsInstrument();
                instrument.MasterInstrument.ThrowOnPointValueRead = true;
                Instrument.All.Add(instrument);

                var provider = new Nt8HostDataProvider(masterMetadataReady: true);
                var results = new M4InstrumentAttestationProbe().Evaluate(Context(provider));

                var field = Find(results, "M4.FIELD_METADATA");
                Assert.Equal(DiagnosticStatus.FAIL, field.Status);
                Assert.Contains(AttestationReasonCodes.HostReadFailed, field.ReasonCodes);

                // The instrument object was still observed, so the object-level check passes
                // independently while the field-level check fails.
                Assert.Equal(DiagnosticStatus.PASS, Find(results, "M4.HOST_METADATA").Status);
            }
            finally
            {
                Reset();
            }
        }

        [Fact]
        public void Attestation_FullyReadMetadata_FieldMetadataPasses()
        {
            Reset();
            try
            {
                Connection.Connections.Add(ConnectedRithmic());
                Instrument.All.Add(EsInstrument());

                var provider = new Nt8HostDataProvider(masterMetadataReady: true);
                var results = new M4InstrumentAttestationProbe().Evaluate(Context(provider));

                var field = Find(results, "M4.FIELD_METADATA");
                Assert.Equal(DiagnosticStatus.PASS, field.Status);
                Assert.Contains("ALL_FIELDS_READ", field.ReasonCodes);
            }
            finally
            {
                Reset();
            }
        }

        // ---------------- probe: read-failure vs unobserved-field vs complete ----------------

        [Fact]
        public void FieldMetadata_ReadFailure_IsFail()
        {
            var metadata = FullyReadMetadata();
            metadata.TickSize = null;
            metadata.ReadFailures.Add("TickSize");

            var results = new M4InstrumentAttestationProbe().Evaluate(AttestationContext(metadata));
            var field = Find(results, "M4.FIELD_METADATA");

            Assert.Equal(DiagnosticStatus.FAIL, field.Status);
            Assert.Contains(AttestationReasonCodes.HostReadFailed, field.ReasonCodes);
        }

        [Fact]
        public void FieldMetadata_OneFailedField_KeepsOtherFieldEvidence()
        {
            var metadata = FullyReadMetadata();
            metadata.TickSize = null;
            metadata.ReadFailures.Add("TickSize");

            var results = new M4InstrumentAttestationProbe().Evaluate(AttestationContext(metadata));
            var field = Find(results, "M4.FIELD_METADATA");

            // Only the failed field is reported; the other fields' read results are still present.
            Assert.Contains("TickSize", field.Observation);
            Assert.DoesNotContain("RootName", field.Observation);
            Assert.DoesNotContain("PointValue", field.Observation);
        }

        [Fact]
        public void FieldMetadata_RequiredFieldNotRead_IsInconclusiveNotPass()
        {
            // No getter threw, but a required master field was never read (for example the
            // master metadata surface was not readable). The field set is incomplete.
            var metadata = FullyReadMetadata();
            metadata.MasterMetadataAvailable = false;
            metadata.RootNameObservable = false;
            metadata.InstrumentTypeObservable = false;
            metadata.CurrencyObservable = false;
            metadata.PointValue = null;
            metadata.TickSize = null;

            var results = new M4InstrumentAttestationProbe().Evaluate(AttestationContext(metadata));
            var field = Find(results, "M4.FIELD_METADATA");

            Assert.NotEqual(DiagnosticStatus.PASS, field.Status);
            Assert.Equal(DiagnosticStatus.INCONCLUSIVE, field.Status);
            Assert.Contains(AttestationReasonCodes.HostMetadataIncomplete, field.ReasonCodes);
        }

        [Fact]
        public void FieldMetadata_FullyObserved_IsPass()
        {
            var results = new M4InstrumentAttestationProbe().Evaluate(AttestationContext(FullyReadMetadata()));
            var field = Find(results, "M4.FIELD_METADATA");

            Assert.Equal(DiagnosticStatus.PASS, field.Status);
            Assert.Contains("ALL_FIELDS_READ", field.ReasonCodes);
        }

        [Fact]
        public void FieldMetadata_NoMetadata_IsUnobservable()
        {
            var results = new M4InstrumentAttestationProbe().Evaluate(AttestationContext(null));
            var field = Find(results, "M4.FIELD_METADATA");

            Assert.Equal(DiagnosticStatus.INCONCLUSIVE, field.Status);
            Assert.Equal("UNOBSERVABLE", string.Join("|", field.ReasonCodes));
        }

        // ---------------- the other M4 host builders stay fail-closed on a throwing getter ----------------

        [Fact]
        public void IdentityBuilder_ThrowingRootGetter_IsInconclusiveNotPass()
        {
            // The prompt names BuildInstrumentIdentityObservation as a primary area. Its reads go
            // through SafeString, but the observation carries explicit observability flags set from
            // the read result (never from a swallowed exception), so a throwing getter leaves the
            // identity unobservable and the verifier stays INCONCLUSIVE rather than PASS.
            Reset();
            try
            {
                Connection.Connections.Add(ConnectedRithmic());
                var instrument = EsInstrument();
                instrument.MasterInstrument.ThrowOnNameRead = true;
                Instrument.All.Add(instrument);

                var provider = new Nt8HostDataProvider(masterMetadataReady: true);
                var observation = provider.GetSnapshot().InstrumentIdentity;

                Assert.False(observation.RootMetadataObservable);
                Assert.Equal(0, observation.MatchingInstrumentCount);
                Assert.Equal(DiagnosticStatus.INCONCLUSIVE, InstrumentIdentityVerifier.Evaluate(observation).Status);
            }
            finally
            {
                Reset();
            }
        }

        [Fact]
        public void IdentityBuilder_ThrowingCurrencyGetter_IsInconclusiveNotPass()
        {
            Reset();
            try
            {
                Connection.Connections.Add(ConnectedRithmic());
                var instrument = EsInstrument();
                instrument.MasterInstrument.ThrowOnCurrencyRead = true;
                Instrument.All.Add(instrument);

                var provider = new Nt8HostDataProvider(masterMetadataReady: true);
                var observation = provider.GetSnapshot().InstrumentIdentity;

                Assert.False(observation.CurrencyObservable);
                Assert.Equal(DiagnosticStatus.INCONCLUSIVE, InstrumentIdentityVerifier.Evaluate(observation).Status);
            }
            finally
            {
                Reset();
            }
        }

        [Fact]
        public void EsProductSpecBuilder_ThrowingGetter_IsInconclusiveNotPass()
        {
            // The prompt names BuildEsProductSpecificationObservation as a primary area. It reads
            // through SafeDouble/SafeString but sets each *Observable flag from HasValue / the read
            // result, so a throwing getter keeps the specification unproven (INCONCLUSIVE).
            Reset();
            try
            {
                Connection.Connections.Add(ConnectedRithmic());
                var instrument = EsInstrument();
                instrument.MasterInstrument.ThrowOnPointValueRead = true;
                Instrument.All.Add(instrument);

                var provider = new Nt8HostDataProvider(masterMetadataReady: true);
                var observation = provider.GetSnapshot().EsProductSpecification;

                Assert.True(observation.InstrumentObserved);
                Assert.False(observation.PointValueObservable);
                Assert.Equal(DiagnosticStatus.INCONCLUSIVE, EsProductSpecificationVerifier.Evaluate(observation).Status);
            }
            finally
            {
                Reset();
            }
        }

        // ---------------- exported evidence preserves the read outcome ----------------

        private static DiagnosticRunResult RunResult()
        {
            return new DiagnosticRunResult
            {
                Mode = DiagnosticMode.Full,
                ModeId = "full",
                SessionId = "rp-20260101-000000-abc123",
                CorrelationId = "corr00000000",
                StartedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                FinishedAtUtc = new DateTime(2026, 1, 1, 0, 0, 2, DateTimeKind.Utc),
                FinalState = RunState.Completed,
                Report = new DiagnosticReport
                {
                    SessionId = "rp-20260101-000000-abc123",
                    CorrelationId = "corr00000000",
                    GeneratedAtUtc = new DateTime(2026, 1, 1, 0, 0, 1, DateTimeKind.Utc)
                }
            };
        }

        [Fact]
        public void InstrumentsJson_ExportsFieldReadFailures()
        {
            var metadata = FullyReadMetadata();
            metadata.TickSize = null;
            metadata.ReadFailures.Add("TickSize");

            var dataset = new EvidenceDataset
            {
                Snapshot = new DiagnosticSnapshot
                {
                    Attestation = new InstrumentAttestationObservation
                    {
                        HostAvailable = true,
                        Metadata = metadata
                    }
                }
            };

            var json = EvidencePackageBuilder.Build(RunResult(), dataset).Read(EvidencePackageCatalog.InstrumentsJson);

            Assert.Contains("\"fieldReadFailures\":[\"TickSize\"]", json);
            Assert.Contains("\"tickSizeObservable\":false", json);
            Assert.Contains("\"masterMetadataAvailable\":true", json);
        }

        [Fact]
        public void InstrumentsJson_ExportsNoReadFailuresWhenFullyRead()
        {
            var dataset = new EvidenceDataset
            {
                Snapshot = new DiagnosticSnapshot
                {
                    Attestation = new InstrumentAttestationObservation
                    {
                        HostAvailable = true,
                        Metadata = FullyReadMetadata()
                    }
                }
            };

            var json = EvidencePackageBuilder.Build(RunResult(), dataset).Read(EvidencePackageCatalog.InstrumentsJson);

            Assert.Contains("\"fieldReadFailures\":[]", json);
            Assert.Contains("\"rootNameObservable\":true", json);
        }
    }
}
