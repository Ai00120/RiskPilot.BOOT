using System;
using System.Collections.Generic;
using NinjaTrader.Cbi;
using RiskPilot.Diagnostics.Nt8.Core.Engine;
using RiskPilot.Diagnostics.Nt8.Core.Engine.Probes;
using RiskPilot.Diagnostics.Nt8.Core.Identity;
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
    /// D-097 acceptance over the OFFLINE NinjaTrader stub surface: the observed host facts are 19
    /// accounts, exactly one exact-name match, and one connected Rithmic connection whose provider
    /// enum slot is <c>Provider14</c> and whose mode is Live. The permanent identity is now
    /// connection <c>Rithmic</c>, provider <c>Provider14 -&gt; Rithmic</c> (BOOT's own compiled
    /// mapping), mode Simulation or Live for the one SIM account, denomination USD.
    /// <para>
    /// These tests prove the production default mapping and the fail-closed boundaries: any other
    /// account, connection name, unresolved slot, mode or unreadable field never yields PASS. They
    /// validate the adapter against the stub surface only; this is NOT proof of real host behaviour.
    /// </para>
    /// </summary>
    [Collection("StubHostState")]
    public class D097IdentityAcceptanceTests
    {
        private const string TargetAccount = "SimRP.BOT";
        private const string TargetConnection = "Rithmic";

        private static void Reset()
        {
            Account.All.Clear();
            Connection.Connections.Clear();
        }

        private static Connection TargetConnectionObject(
            Provider provider = Provider.Provider14,
            string name = TargetConnection,
            Mode mode = Mode.Live)
        {
            return new Connection
            {
                Status = ConnectionStatus.Connected,
                PriceStatus = ConnectionStatus.Connected,
                Options = new ConnectOptions
                {
                    Name = name,
                    Provider = provider,
                    Mode = mode
                }
            };
        }

        private static Account AccountNamed(string name, Connection connection, Provider provider = Provider.Provider14)
        {
            return new Account
            {
                Name = name,
                Provider = provider,
                Denomination = Currency.UsDollar,
                Connection = connection
            };
        }

        /// <summary>
        /// Seeds 18 unrelated accounts before the exact target, so enumeration order can never make
        /// the first account look like the target.
        /// </summary>
        private static Account SeedNineteenAccounts(
            Connection connection,
            string targetName = TargetAccount,
            Provider targetProvider = Provider.Provider14,
            bool throwOnDenomination = false)
        {
            for (var i = 1; i <= 18; i++)
            {
                Account.All.Add(AccountNamed("Unrelated Account " + i.ToString("00"), connection));
            }

            var target = AccountNamed(targetName, connection, targetProvider);
            target.ThrowOnDenominationRead = throwOnDenomination;
            Account.All.Add(target);
            return target;
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

        private static ProbeContext Context(FakeDataProvider provider)
        {
            var now = DateTime.UtcNow;
            return new ProbeContext(
                provider,
                new SessionContext(SessionId.New(now), now),
                new FakeClock(now),
                "d0970000000");
        }

        private static FakeDataProvider FakeFrom(Nt8HostDataProvider host)
        {
            var fake = new FakeDataProvider { Snapshot = host.GetSnapshot() };
            foreach (var connection in host.GetConnections())
            {
                fake.Connections.Add(connection);
            }

            foreach (var account in host.GetAccounts())
            {
                fake.Accounts.Add(account);
            }

            return fake;
        }

        [Fact]
        public void Host_19Accounts_ExactTarget_OneRithmicConnection_M2ChecksPass()
        {
            Reset();
            try
            {
                var connection = TargetConnectionObject();
                SeedNineteenAccounts(connection);
                Connection.Connections.Add(connection);

                var host = TestHost.ProductionProvider(true);
                var snapshot = host.GetSnapshot();

                Assert.Equal(19, snapshot.AccountIdentity.ObservedAccountCount);
                Assert.Equal(1, snapshot.AccountIdentity.MatchingAccountCount);
                Assert.True(snapshot.AccountIdentity.ModeIsAccepted);
                Assert.False(snapshot.AccountIdentity.ModeIsSimulation);
                Assert.Equal(DiagnosticStatus.PASS, AccountIdentityVerifier.Evaluate(snapshot.AccountIdentity).Status);
                Assert.Equal(DiagnosticStatus.PASS, TargetAccountConnectionVerifier.Evaluate(snapshot.TargetAccountConnection).Status);

                Assert.True(snapshot.Provider.ProviderResolved);
                Assert.True(snapshot.Provider.MatchesExpectedProvider);
                Assert.Equal(ProviderResolutionBasis.TargetAccountConnection, snapshot.Provider.ResolutionBasis);

                var results = new M2ConnectionProbe().Evaluate(Context(FakeFrom(host)));
                Assert.Equal(DiagnosticStatus.PASS, Find(results, "M2.ACCOUNT_IDENTITY").Status);
                Assert.Equal(DiagnosticStatus.PASS, Find(results, "M2.ACCOUNT_CONNECTION_ASSOCIATION").Status);
                Assert.Equal(DiagnosticStatus.PASS, Find(results, "M2.PROVIDER_IDENTITY").Status);
                Assert.Equal(DiagnosticStatus.PASS, Find(results, "M2.ACCOUNT_PROVIDER_AGREEMENT").Status);
            }
            finally
            {
                Reset();
            }
        }

        [Fact]
        public void Host_19Accounts_NoExactTarget_IsNeverPass()
        {
            Reset();
            try
            {
                var connection = TargetConnectionObject();
                SeedNineteenAccounts(connection, targetName: "SimRP.BOT2");
                Connection.Connections.Add(connection);

                var host = TestHost.ProductionProvider(true);
                var snapshot = host.GetSnapshot();

                Assert.Equal(19, snapshot.AccountIdentity.ObservedAccountCount);
                Assert.Equal(0, snapshot.AccountIdentity.MatchingAccountCount);
                Assert.NotEqual(DiagnosticStatus.PASS, AccountIdentityVerifier.Evaluate(snapshot.AccountIdentity).Status);

                var results = new M2ConnectionProbe().Evaluate(Context(FakeFrom(host)));
                Assert.NotEqual(DiagnosticStatus.PASS, Find(results, "M2.ACCOUNT_IDENTITY").Status);
                Assert.NotEqual(DiagnosticStatus.PASS, Find(results, "M2.ACCOUNT_CONNECTION_ASSOCIATION").Status);
            }
            finally
            {
                Reset();
            }
        }

        [Fact]
        public void Host_WrongAccountProvider_IsFail()
        {
            Reset();
            try
            {
                var connection = TargetConnectionObject();
                SeedNineteenAccounts(connection, targetProvider: Provider.Simulator);
                Connection.Connections.Add(connection);

                var host = TestHost.ProductionProvider(true);
                var verdict = AccountIdentityVerifier.Evaluate(host.GetSnapshot().AccountIdentity);

                Assert.Equal(DiagnosticStatus.FAIL, verdict.Status);
                Assert.Contains(AccountIdentityReasonCodes.AccountProviderMismatch, verdict.MismatchedFields);
            }
            finally
            {
                Reset();
            }
        }

        [Fact]
        public void Host_WrongConnectionName_IsFail()
        {
            Reset();
            try
            {
                var connection = TargetConnectionObject(name: "Other Rithmic");
                SeedNineteenAccounts(connection);
                Connection.Connections.Add(connection);

                var host = TestHost.ProductionProvider(true);
                var verdict = TargetAccountConnectionVerifier.Evaluate(host.GetSnapshot().TargetAccountConnection);

                Assert.Equal(DiagnosticStatus.FAIL, verdict.Status);
                Assert.Contains(TargetAccountConnectionReasonCodes.ConnectionNameMismatch, verdict.MismatchedFields);
            }
            finally
            {
                Reset();
            }
        }

        [Fact]
        public void Host_UnresolvedProviderSlot_IsNeverPass()
        {
            Reset();
            try
            {
                // Provider15 is a placeholder slot that BOOT's compiled mapping does not authorize.
                var connection = TargetConnectionObject(provider: Provider.Provider15);
                SeedNineteenAccounts(connection, targetProvider: Provider.Provider15);
                Connection.Connections.Add(connection);

                var host = TestHost.ProductionProvider(true);
                var snapshot = host.GetSnapshot();

                Assert.False(snapshot.Provider.ProviderResolved);
                Assert.NotEqual(DiagnosticStatus.PASS, AccountIdentityVerifier.Evaluate(snapshot.AccountIdentity).Status);
                Assert.NotEqual(DiagnosticStatus.PASS, TargetAccountConnectionVerifier.Evaluate(snapshot.TargetAccountConnection).Status);

                var results = new M2ConnectionProbe().Evaluate(Context(FakeFrom(host)));
                Assert.NotEqual(DiagnosticStatus.PASS, Find(results, "M2.PROVIDER_IDENTITY").Status);
            }
            finally
            {
                Reset();
            }
        }

        [Fact]
        public void Host_WrongMode_IsFail()
        {
            Reset();
            try
            {
                var connection = TargetConnectionObject(mode: (Mode)99);
                SeedNineteenAccounts(connection);
                Connection.Connections.Add(connection);

                var host = TestHost.ProductionProvider(true);
                var verdict = AccountIdentityVerifier.Evaluate(host.GetSnapshot().AccountIdentity);

                Assert.Equal(DiagnosticStatus.FAIL, verdict.Status);
                Assert.Contains(AccountIdentityReasonCodes.ModeMismatch, verdict.MismatchedFields);
            }
            finally
            {
                Reset();
            }
        }

        [Fact]
        public void Host_NonUsdDenomination_IsFail()
        {
            Reset();
            try
            {
                var connection = TargetConnectionObject();
                var target = SeedNineteenAccounts(connection);
                target.Denomination = Currency.JapaneseYen;
                Connection.Connections.Add(connection);

                var host = TestHost.ProductionProvider(true);
                var verdict = AccountIdentityVerifier.Evaluate(host.GetSnapshot().AccountIdentity);

                Assert.Equal(DiagnosticStatus.FAIL, verdict.Status);
                Assert.Contains(AccountIdentityReasonCodes.CurrencyMismatch, verdict.MismatchedFields);
            }
            finally
            {
                Reset();
            }
        }

        [Fact]
        public void Host_UnreadableDenomination_IsInconclusive_NeverPass()
        {
            Reset();
            try
            {
                var connection = TargetConnectionObject();
                SeedNineteenAccounts(connection, throwOnDenomination: true);
                Connection.Connections.Add(connection);

                var host = TestHost.ProductionProvider(true);
                var observation = host.GetSnapshot().AccountIdentity;

                Assert.False(observation.DenominationObservable);
                var verdict = AccountIdentityVerifier.Evaluate(observation);
                Assert.Equal(DiagnosticStatus.INCONCLUSIVE, verdict.Status);
                Assert.Contains(AccountIdentityReasonCodes.CurrencyUnobservable, verdict.UnobservableFields);
            }
            finally
            {
                Reset();
            }
        }
    }
}
