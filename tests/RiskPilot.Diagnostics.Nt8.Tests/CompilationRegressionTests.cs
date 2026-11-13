using System;
using System.Collections.Generic;
using NinjaTrader.Cbi;
using RiskPilot.Diagnostics.Nt8.Core.Identity;
using RiskPilot.Diagnostics.Nt8.Core.Models.Observations;
using RiskPilot.Diagnostics.Nt8.Core.Status;
using RiskPilot.Diagnostics.Nt8.Host;
using Xunit;

namespace RiskPilot.Diagnostics.Nt8.Tests
{
    /// <summary>
    /// Regression tests for the two NinjaTrader 8.1.9.0 compilation fixes.
    /// <para>
    /// Issue 2: the real NT8 <c>NinjaTrader.Cbi.Currency</c> enum has no <c>Yen</c> member
    /// (JPY is <c>JapaneseYen</c>), and the approved ES/USD identity accepts USD only. These
    /// tests drive the offline stub surface, the real host adapter and the real verifiers: USD
    /// is accepted, an observed non-USD currency is a definite FAIL, and a missing/unavailable
    /// denomination or product currency can never produce a false PASS.
    /// </para>
    /// <para>
    /// Issue 1 (duplicate assembly attributes) is covered by
    /// <c>verification/addon-smoke/test_nt8_compilation_regression.py</c>, which stages the
    /// NinjaScript source bundle and proves no AssemblyInfo.cs reaches the Custom tree.
    /// These C# tests do not claim real NinjaTrader host compatibility.
    /// </para>
    /// </summary>
    [Collection("StubHostState")]
    public class CompilationRegressionTests
    {
        private const string ExpectedAccount = "SimRP.BOT";
        private const string ExpectedConnection = "My Rithmic";

        private static void Reset()
        {
            Account.All.Clear();
            Connection.Connections.Clear();
            Instrument.All.Clear();
        }

        private static Connection ConnectedRithmic()
        {
            return new Connection
            {
                Status = ConnectionStatus.Connected,
                PriceStatus = ConnectionStatus.Connected,
                Options = new ConnectOptions
                {
                    Name = ExpectedConnection,
                    Provider = Provider.Rithmic,
                    Mode = Mode.Simulation
                }
            };
        }

        private static Account IdentityAccount(Currency denomination)
        {
            return new Account
            {
                Name = ExpectedAccount,
                Provider = Provider.Rithmic,
                Denomination = denomination,
                Connection = ConnectedRithmic()
            };
        }

        private static Instrument EsInstrument(Currency currency)
        {
            return new Instrument
            {
                FullName = "ES 12-26",
                Exchange = Exchange.Globex,
                MasterInstrument = new MasterInstrument
                {
                    Name = "ES",
                    InstrumentType = InstrumentType.Future,
                    Currency = currency,
                    TickSize = 0.25,
                    PointValue = 50,
                    Exchanges = new List<Exchange> { Exchange.Globex }
                }
            };
        }

        /// <summary>An otherwise fully observed, fully matching account identity observation.</summary>
        private static AccountIdentityObservation ExactAccountObservation()
        {
            return new AccountIdentityObservation
            {
                AccountSurfaceObservable = true,
                IsStubOrMockValue = false,
                ObservedAccountCount = 1,
                MatchingAccountCount = 1,
                AccountRef = "account#regression",
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
            };
        }

        [Fact]
        public void StubCurrencyEnum_MatchesRealNt8Api_AndHasNoYenMember()
        {
            // Verified against the reflected NT8 NinjaTrader.Cbi.Currency enum: there is no
            // 'Yen' member and no 'BritishPounds' member. The numeric values keep the offline
            // stub aligned with the real host enum surface.
            var names = Enum.GetNames(typeof(Currency));

            Assert.Contains("UsDollar", names);
            Assert.Contains("JapaneseYen", names);
            Assert.Contains("BritishPound", names);
            Assert.Contains("Euro", names);
            Assert.DoesNotContain("Yen", names);
            Assert.DoesNotContain("BritishPounds", names);
            Assert.Equal(1, (int)Currency.BritishPound);
            Assert.Equal(3, (int)Currency.Euro);
            Assert.Equal(5, (int)Currency.JapaneseYen);
            Assert.Equal(7, (int)Currency.UsDollar);
        }

        [Fact]
        public void UsdDenomination_IsAccepted_AndNoOtherCurrencyIsConvertedToUsd()
        {
            Reset();
            try
            {
                Account.All.Add(IdentityAccount(Currency.UsDollar));
                var provider = new Nt8HostDataProvider(masterMetadataReady: false);
                var observation = provider.GetSnapshot().AccountIdentity;

                Assert.True(observation.DenominationObservable);
                Assert.True(observation.DenominationIsUsd);
                Assert.Equal(DiagnosticStatus.PASS, AccountIdentityVerifier.Evaluate(observation).Status);

                // The identity comparator accepts exactly USD; a foreign ISO code is never
                // normalized into USD.
                Assert.True(ExpectedAccountIdentity.CurrencyIsUsd("USD"));
                Assert.False(ExpectedAccountIdentity.CurrencyIsUsd("EUR"));
                Assert.False(ExpectedAccountIdentity.CurrencyIsUsd("JPY"));
                Assert.False(ExpectedAccountIdentity.CurrencyIsUsd("GBP"));
            }
            finally
            {
                Reset();
            }
        }

        [Fact]
        public void NonUsdDenomination_IsObservedMismatch_AndFails()
        {
            Reset();
            try
            {
                Account.All.Add(IdentityAccount(Currency.Euro));
                var provider = new Nt8HostDataProvider(masterMetadataReady: false);
                var observation = provider.GetSnapshot().AccountIdentity;

                // A real, observed non-USD member must stay observable (a definite contradiction),
                // not be dropped as "unmapped" and not be converted into USD.
                Assert.True(observation.DenominationObservable);
                Assert.False(observation.DenominationIsUsd);

                var verdict = AccountIdentityVerifier.Evaluate(observation);
                Assert.Equal(DiagnosticStatus.FAIL, verdict.Status);
                Assert.Contains(AccountIdentityReasonCodes.CurrencyMismatch, verdict.ReasonCodes);
            }
            finally
            {
                Reset();
            }
        }

        [Fact]
        public void UnavailableDenomination_HostReadFailure_IsInconclusive()
        {
            Reset();
            try
            {
                var account = IdentityAccount(Currency.UsDollar);
                account.ThrowOnDenominationRead = true;
                Account.All.Add(account);
                var observation = new Nt8HostDataProvider(false).GetSnapshot().AccountIdentity;
                Assert.False(observation.DenominationObservable);
                Assert.False(observation.DenominationIsUsd);
                var verdict = AccountIdentityVerifier.Evaluate(observation);
                Assert.Equal(DiagnosticStatus.INCONCLUSIVE, verdict.Status);
                Assert.Contains(AccountIdentityReasonCodes.CurrencyUnobservable, verdict.ReasonCodes);
            }
            finally { Reset(); }
        }

        [Fact]
        public void MissingDenomination_IsInconclusive_AndNeverPasses()
        {
            // A denomination that could not be read must not produce a PASS even if a stale
            // DenominationIsUsd flag is set: the observable flag is authoritative.
            var observation = ExactAccountObservation();
            observation.DenominationObservable = false;
            observation.DenominationIsUsd = true;

            var verdict = AccountIdentityVerifier.Evaluate(observation);

            Assert.Equal(DiagnosticStatus.INCONCLUSIVE, verdict.Status);
            Assert.NotEqual(DiagnosticStatus.PASS, verdict.Status);
            Assert.Contains(AccountIdentityReasonCodes.CurrencyUnobservable, verdict.ReasonCodes);
            Assert.Contains(AccountIdentityReasonCodes.CurrencyUnobservable, verdict.UnobservableFields);
        }

        [Fact]
        public void NonUsdInstrumentCurrency_IsObservedMismatch_AndFails()
        {
            Reset();
            try
            {
                Connection.Connections.Add(ConnectedRithmic());
                Instrument.All.Add(EsInstrument(Currency.Euro));

                var provider = new Nt8HostDataProvider(masterMetadataReady: true);
                var observation = provider.GetSnapshot().InstrumentIdentity;

                Assert.True(observation.CurrencyObservable);
                Assert.False(observation.CurrencyMatches);

                var verdict = InstrumentIdentityVerifier.Evaluate(observation);
                Assert.Equal(DiagnosticStatus.FAIL, verdict.Status);
                Assert.Contains(InstrumentIdentityReasonCodes.CurrencyMismatch, verdict.ReasonCodes);
            }
            finally
            {
                Reset();
            }
        }

        [Fact]
        public void NonUsdEsProductCurrency_IsObservedMismatch_AndFails()
        {
            Reset();
            try
            {
                Connection.Connections.Add(ConnectedRithmic());
                Instrument.All.Add(EsInstrument(Currency.Euro));

                var provider = new Nt8HostDataProvider(masterMetadataReady: true);
                var observation = provider.GetSnapshot().EsProductSpecification;

                Assert.True(observation.CurrencyObservable);
                Assert.NotEqual(ExpectedEsProductSpecification.Currency, observation.Currency);
                Assert.Equal("Euro", observation.Currency); // raw host member, never converted to USD

                var verdict = EsProductSpecificationVerifier.Evaluate(observation);
                Assert.Equal(DiagnosticStatus.FAIL, verdict.Status);
                Assert.Contains(EsProductSpecificationReasonCodes.CurrencyMismatch, verdict.ReasonCodes);
            }
            finally
            {
                Reset();
            }
        }

        [Fact]
        public void MissingInstrumentCurrency_IsInconclusive_AndNeverPasses()
        {
            Reset();
            try
            {
                Connection.Connections.Add(ConnectedRithmic());
                var instrument = EsInstrument(Currency.UsDollar);
                instrument.MasterInstrument = null; // product currency (and economics) unavailable
                Instrument.All.Add(instrument);

                var provider = new Nt8HostDataProvider(masterMetadataReady: true);
                var observation = provider.GetSnapshot().InstrumentIdentity;

                Assert.False(observation.CurrencyObservable);
                var verdict = InstrumentIdentityVerifier.Evaluate(observation);
                Assert.Equal(DiagnosticStatus.INCONCLUSIVE, verdict.Status);
                Assert.NotEqual(DiagnosticStatus.PASS, verdict.Status);
            }
            finally
            {
                Reset();
            }
        }
    }
}
