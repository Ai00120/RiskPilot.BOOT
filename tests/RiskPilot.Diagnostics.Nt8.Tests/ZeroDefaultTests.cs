using System;
using System.Collections.Generic;
using NinjaTrader.Cbi;
using RiskPilot.Diagnostics.Nt8.Core.Evidence;
using RiskPilot.Diagnostics.Nt8.Core.Models;
using RiskPilot.Diagnostics.Nt8.Core.Report;
using RiskPilot.Diagnostics.Nt8.Core.Result;
using RiskPilot.Diagnostics.Nt8.Core.Run;
using RiskPilot.Diagnostics.Nt8.Core.Status;
using RiskPilot.Diagnostics.Nt8.Host;
using Xunit;

namespace RiskPilot.Diagnostics.Nt8.Tests
{
    /// <summary>
    /// Zero-default regression tests. A value the host read and that is genuinely zero must be
    /// exported as <c>0</c>; a missing, null, unavailable or unreadable value must stay explicitly
    /// unknown (null in the model, <c>UNOBSERVABLE</c> in the JSON), and a null collection must
    /// never be reported as a confirmed empty collection with a count of zero.
    /// <para>
    /// The host-adapter cases run over the OFFLINE NinjaTrader stub surface; a green stub run is
    /// NOT proof of real host behaviour.
    /// </para>
    /// </summary>
    public class ZeroDefaultTests
    {
        private static DiagnosticRunResult RunResult()
        {
            var report = new DiagnosticReport
            {
                SessionId = "rp-20260101-000000-abc123",
                CorrelationId = "corr00000000",
                GeneratedAtUtc = new DateTime(2026, 1, 1, 0, 0, 1, DateTimeKind.Utc)
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
                Report = report
            };
        }

        private static string ReadAccounts(EvidenceDataset dataset)
        {
            return EvidencePackageBuilder.Build(RunResult(), dataset).Read(EvidencePackageCatalog.AccountsJson);
        }

        private static string ReadInstruments(EvidenceDataset dataset)
        {
            return EvidencePackageBuilder.Build(RunResult(), dataset).Read(EvidencePackageCatalog.InstrumentsJson);
        }

        // ---------------- model: genuine zero is distinct from unknown ----------------

        [Fact]
        public void AccountRecord_GenuineZero_HasValue()
        {
            var record = new AccountRecord { PositionCount = 0, WorkingOrderCount = 0 };
            Assert.True(record.PositionCount.HasValue);
            Assert.True(record.WorkingOrderCount.HasValue);
            Assert.Equal(0, record.PositionCount);
            Assert.Equal(0, record.WorkingOrderCount);
        }

        [Fact]
        public void AccountRecord_Unreadable_IsNullNotZero()
        {
            var record = new AccountRecord();
            Assert.Null(record.PositionCount);
            Assert.Null(record.WorkingOrderCount);
        }

        [Fact]
        public void InstrumentRecord_GenuineZero_HasValue()
        {
            var record = new InstrumentRecord { TickSize = 0d, PointValue = 0d };
            Assert.True(record.TickSize.HasValue);
            Assert.True(record.PointValue.HasValue);
            Assert.Equal(0d, record.TickSize);
            Assert.Equal(0d, record.PointValue);
        }

        [Fact]
        public void InstrumentRecord_Unreadable_IsNullNotZero()
        {
            var record = new InstrumentRecord();
            Assert.Null(record.TickSize);
            Assert.Null(record.PointValue);
        }

        // ---------------- evidence JSON: genuine zero ----------------

        [Fact]
        public void AccountsJson_GenuineZero_ExportsNumericZero()
        {
            var dataset = new EvidenceDataset();
            dataset.Accounts.Add(new AccountRecord
            {
                AccountRef = "ACC-0001",
                PositionCount = 0,
                WorkingOrderCount = 0
            });

            var json = ReadAccounts(dataset);
            Assert.Contains("\"positionCount\":0", json);
            Assert.Contains("\"workingOrderCount\":0", json);
            Assert.DoesNotContain("\"positionCount\":\"UNOBSERVABLE\"", json);
            Assert.DoesNotContain("\"workingOrderCount\":\"UNOBSERVABLE\"", json);
        }

        [Fact]
        public void InstrumentsJson_GenuineZeroEconomics_ExportsNumericZero()
        {
            var dataset = new EvidenceDataset();
            dataset.Instruments.Add(new InstrumentRecord { TickSize = 0d, PointValue = 0d });

            var json = ReadInstruments(dataset);
            Assert.Contains("\"tickSize\":0", json);
            Assert.Contains("\"pointValue\":0", json);
            Assert.DoesNotContain("\"tickSize\":\"UNOBSERVABLE\"", json);
        }

        // ---------------- evidence JSON: unreadable value stays unknown ----------------

        [Fact]
        public void AccountsJson_UnreadableCount_ExportsUnobservableNotZero()
        {
            var dataset = new EvidenceDataset();
            dataset.Accounts.Add(new AccountRecord { AccountRef = "ACC-0001" });

            var json = ReadAccounts(dataset);
            Assert.Contains("\"positionCount\":\"UNOBSERVABLE\"", json);
            Assert.Contains("\"workingOrderCount\":\"UNOBSERVABLE\"", json);
            Assert.DoesNotContain("\"positionCount\":0", json);
            Assert.DoesNotContain("\"workingOrderCount\":0", json);
        }

        [Fact]
        public void InstrumentsJson_UnreadableEconomics_ExportsUnobservableNotZero()
        {
            var dataset = new EvidenceDataset();
            dataset.Instruments.Add(new InstrumentRecord { MasterMetadataAvailable = true });

            var json = ReadInstruments(dataset);
            Assert.Contains("\"tickSize\":\"UNOBSERVABLE\"", json);
            Assert.Contains("\"pointValue\":\"UNOBSERVABLE\"", json);
            Assert.DoesNotContain("\"tickSize\":0", json);
            Assert.DoesNotContain("\"pointValue\":0", json);
        }

        // ---------------- collections: null / empty are not a zero count ----------------

        [Fact]
        public void AccountsJson_NullCollection_IsUnavailableWithUnobservableCount()
        {
            var dataset = new EvidenceDataset { Accounts = null };
            var json = ReadAccounts(dataset);
            Assert.Contains("\"count\":\"UNOBSERVABLE\"", json);
            Assert.Contains("UNAVAILABLE", json);
            Assert.DoesNotContain("\"count\":0", json);
        }

        [Fact]
        public void AccountsJson_EmptyCollection_IsUnavailableNotZeroCount()
        {
            var json = ReadAccounts(new EvidenceDataset());
            Assert.Contains("\"count\":\"UNOBSERVABLE\"", json);
            Assert.Contains("UNAVAILABLE", json);
            Assert.DoesNotContain("\"count\":0", json);
        }

        [Fact]
        public void AccountsJson_ObservedCollection_ExportsRealCount()
        {
            var dataset = new EvidenceDataset();
            dataset.Accounts.Add(new AccountRecord { AccountRef = "ACC-0001", PositionCount = 1 });

            var json = ReadAccounts(dataset);
            Assert.Contains("\"count\":1", json);
            Assert.Contains("AVAILABLE", json);
        }

        [Fact]
        public void InstrumentsJson_NullCollection_IsUnavailableNotZeroCount()
        {
            var dataset = new EvidenceDataset { Instruments = null };
            var json = ReadInstruments(dataset);
            Assert.Contains("\"count\":\"UNOBSERVABLE\"", json);
            Assert.Contains("UNAVAILABLE", json);
            Assert.DoesNotContain("\"count\":0", json);
        }

        [Fact]
        public void InstrumentsJson_EmptyCollection_IsUnavailableNotZeroCount()
        {
            var json = ReadInstruments(new EvidenceDataset());
            Assert.Contains("\"count\":\"UNOBSERVABLE\"", json);
            Assert.Contains("UNAVAILABLE", json);
            Assert.DoesNotContain("\"count\":0", json);
        }

        [Fact]
        public void InstrumentsJson_ObservedCollection_ExportsRealCount()
        {
            var dataset = new EvidenceDataset();
            dataset.Instruments.Add(new InstrumentRecord { FullName = "ES 12-26", TickSize = 0.25, PointValue = 50d });

            var json = ReadInstruments(dataset);
            Assert.Contains("\"count\":1", json);
            Assert.Contains("AVAILABLE", json);
        }

        // ---------------- host adapter over the offline stub surface ----------------

        [Collection("StubHostState")]
        public class HostAdapter
        {
            private static void Reset()
            {
                Account.All.Clear();
                Connection.Connections.Clear();
                Instrument.All.Clear();
            }

            private static Account VerifiedTargetAccount()
            {
                return new Account
                {
                    Name = RiskPilot.Diagnostics.Nt8.Core.Identity.ExpectedAccountIdentity.AccountName,
                    Provider = Provider.Rithmic,
                    Denomination = Currency.UsDollar,
                    Connection = new Connection
                    {
                        Status = ConnectionStatus.Connected,
                        PriceStatus = ConnectionStatus.Connected,
                        Options = new ConnectOptions
                        {
                            Name = RiskPilot.Diagnostics.Nt8.Core.Identity.ExpectedAccountIdentity.ConnectionName,
                            Provider = Provider.Rithmic,
                            Mode = Mode.Simulation
                        }
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

            [Fact]
            public void GetAccounts_ReadableEmptyCollections_ReportZero()
            {
                Reset();
                try
                {
                    Account.All.Add(VerifiedTargetAccount());

                    var accounts = new Nt8HostDataProvider(masterMetadataReady: false).GetAccounts();

                    Assert.Single(accounts);
                    Assert.Equal(0, accounts[0].PositionCount);
                    Assert.Equal(0, accounts[0].WorkingOrderCount);
                }
                finally
                {
                    Reset();
                }
            }

            [Fact]
            public void GetAccounts_UnreadableCollections_ReportUnknownNotZero()
            {
                Reset();
                try
                {
                    var account = VerifiedTargetAccount();
                    account.ThrowOnPositionsRead = true;
                    account.ThrowOnOrdersRead = true;
                    Account.All.Add(account);

                    var accounts = new Nt8HostDataProvider(masterMetadataReady: false).GetAccounts();

                    Assert.Single(accounts);
                    Assert.Null(accounts[0].PositionCount);
                    Assert.Null(accounts[0].WorkingOrderCount);
                }
                finally
                {
                    Reset();
                }
            }

            [Fact]
            public void GetAccounts_ReadableEmptyCollections_ExportNumericZero()
            {
                Reset();
                try
                {
                    // A verified target with readable-but-empty Portfolio collections is a
                    // CONFIRMED empty collection: the exported counts are a genuine 0, not
                    // UNOBSERVABLE. This is the end-to-end proof of the zero-vs-unknown rule.
                    Account.All.Add(VerifiedTargetAccount());

                    var dataset = new EvidenceDataset();
                    foreach (var record in new Nt8HostDataProvider(masterMetadataReady: false).GetAccounts())
                    {
                        dataset.Accounts.Add(record);
                    }

                    var json = ReadAccounts(dataset);
                    Assert.Contains("\"positionCount\":0", json);
                    Assert.Contains("\"workingOrderCount\":0", json);
                    Assert.DoesNotContain("\"positionCount\":\"UNOBSERVABLE\"", json);
                    Assert.DoesNotContain("\"workingOrderCount\":\"UNOBSERVABLE\"", json);
                }
                finally
                {
                    Reset();
                }
            }

            [Fact]
            public void GetAccounts_UnreadableCounts_ExportUnobservable()
            {
                Reset();
                try
                {
                    var account = VerifiedTargetAccount();
                    account.ThrowOnPositionsRead = true;
                    account.ThrowOnOrdersRead = true;
                    Account.All.Add(account);

                    var dataset = new EvidenceDataset();
                    foreach (var record in new Nt8HostDataProvider(masterMetadataReady: false).GetAccounts())
                    {
                        dataset.Accounts.Add(record);
                    }

                    var json = ReadAccounts(dataset);
                    Assert.Contains("\"positionCount\":\"UNOBSERVABLE\"", json);
                    Assert.Contains("\"workingOrderCount\":\"UNOBSERVABLE\"", json);
                    Assert.DoesNotContain("\"positionCount\":0", json);
                }
                finally
                {
                    Reset();
                }
            }

            [Fact]
            public void GetInstrument_GenuineZeroEconomics_ReportZero()
            {
                Reset();
                try
                {
                    var instrument = EsInstrument();
                    instrument.MasterInstrument.TickSize = 0;
                    instrument.MasterInstrument.PointValue = 0;
                    Instrument.All.Add(instrument);

                    var record = new Nt8HostDataProvider(masterMetadataReady: true).GetInstrument("ES 12-26");

                    Assert.NotNull(record);
                    Assert.True(record.MasterMetadataAvailable);
                    Assert.Equal(0d, record.TickSize);
                    Assert.Equal(0d, record.PointValue);
                }
                finally
                {
                    Reset();
                }
            }

            [Fact]
            public void GetInstrument_UnreadableEconomics_ReportUnknownNotZero()
            {
                Reset();
                try
                {
                    var instrument = EsInstrument();
                    instrument.MasterInstrument.ThrowOnTickSizeRead = true;
                    instrument.MasterInstrument.ThrowOnPointValueRead = true;
                    Instrument.All.Add(instrument);

                    var record = new Nt8HostDataProvider(masterMetadataReady: true).GetInstrument("ES 12-26");

                    Assert.NotNull(record);
                    Assert.True(record.MasterMetadataAvailable);
                    Assert.Null(record.TickSize);
                    Assert.Null(record.PointValue);
                }
                finally
                {
                    Reset();
                }
            }

            [Fact]
            public void GetInstrument_UnreadableEconomics_ExportUnobservable()
            {
                Reset();
                try
                {
                    var instrument = EsInstrument();
                    instrument.MasterInstrument.ThrowOnTickSizeRead = true;
                    instrument.MasterInstrument.ThrowOnPointValueRead = true;
                    Instrument.All.Add(instrument);

                    var dataset = new EvidenceDataset();
                    dataset.Instruments.Add(new Nt8HostDataProvider(masterMetadataReady: true).GetInstrument("ES 12-26"));

                    var json = ReadInstruments(dataset);
                    Assert.Contains("\"tickSize\":\"UNOBSERVABLE\"", json);
                    Assert.Contains("\"pointValue\":\"UNOBSERVABLE\"", json);
                    Assert.DoesNotContain("\"tickSize\":0", json);
                }
                finally
                {
                    Reset();
                }
            }

            [Fact]
            public void GetInstrument_AbsentInstrument_ReturnsNull()
            {
                Reset();
                try
                {
                    var record = new Nt8HostDataProvider(masterMetadataReady: true).GetInstrument("ES 12-26");
                    Assert.Null(record);
                }
                finally
                {
                    Reset();
                }
            }

            [Fact]
            public void GetAccounts_NoVerifiedTarget_ReturnsNoAccount()
            {
                Reset();
                try
                {
                    // No account at all: the surface is not observable, so no account record (and
                    // therefore no zero count) is produced.
                    var accounts = new Nt8HostDataProvider(masterMetadataReady: false).GetAccounts();
                    Assert.Empty(accounts);

                    var dataset = new EvidenceDataset();
                    foreach (var record in accounts)
                    {
                        dataset.Accounts.Add(record);
                    }

                    var json = ReadAccounts(dataset);
                    Assert.Contains("\"count\":\"UNOBSERVABLE\"", json);
                    Assert.Contains("UNAVAILABLE", json);
                    Assert.DoesNotContain("\"count\":0", json);
                }
                finally
                {
                    Reset();
                }
            }
        }
    }
}
