using System.Collections.Generic;
using NinjaTrader.Cbi;
using RiskPilot.Diagnostics.Nt8.Core.Identity;
using RiskPilot.Diagnostics.Nt8.Core.Models.Observations;
using RiskPilot.Diagnostics.Nt8.Host;
using RiskPilot.Diagnostics.Nt8.Core.Status;
using Xunit;

namespace RiskPilot.Diagnostics.Nt8.Tests
{
    /// <summary>
    /// Tests for the host-layer account-identity adapter over the OFFLINE NinjaTrader stub
    /// surface. They exercise the real <see cref="Nt8HostDataProvider"/> mapping code (exact
    /// ordinal name comparison, provider/mode/currency mapping) so a regression in the adapter
    /// is caught offline. This validates the adapter against the stub surface only; it is NOT
    /// proof of real host behaviour (see the offline build caveat).
    /// </summary>
    public class HostAccountIdentityTests
    {
        private const string ExpectedAccount = "SimRP.BOT";
        private const string ExpectedConnection = "My Rithmic";

        private static void Reset()
        {
            Account.All.Clear();
            Connection.Connections.Clear();
        }

        private static Account IdentityAccount(string name)
        {
            return new Account
            {
                Name = name,
                Provider = Provider.Rithmic,
                Denomination = Currency.UsDollar,
                Connection = new Connection
                {
                    Status = ConnectionStatus.Connected,
                    PriceStatus = ConnectionStatus.Connected,
                    Options = new ConnectOptions
                    {
                        Name = ExpectedConnection,
                        Provider = Provider.Rithmic,
                        Mode = Mode.Simulation
                    }
                }
            };
        }

        [Fact]
        public void Host_ExactIdentity_ProducesPassingObservation()
        {
            Reset();
            try
            {
                Account.All.Add(IdentityAccount(ExpectedAccount));
                var provider = new Nt8HostDataProvider(masterMetadataReady: false);

                var observation = provider.GetSnapshot().AccountIdentity;

                Assert.True(observation.AccountSurfaceObservable);
                Assert.Equal(1, observation.ObservedAccountCount);
                Assert.Equal(1, observation.MatchingAccountCount);
                Assert.True(observation.AccountProviderMatches);
                Assert.True(observation.ConnectionObserved);
                Assert.True(observation.ConnectionNameMatches);
                Assert.True(observation.ConnectionProviderMatches);
                Assert.True(observation.ConnectionConnected);
                Assert.True(observation.ModeIsSimulation);
                Assert.True(observation.DenominationIsUsd);
                Assert.Equal(DiagnosticStatus.PASS, AccountIdentityVerifier.Evaluate(observation).Status);
            }
            finally
            {
                Reset();
            }
        }

        [Fact]
        public void Host_SimilarName_IsNotAnExactMatch()
        {
            Reset();
            try
            {
                // A look-alike name (trailing character) must not count as an exact match.
                Account.All.Add(IdentityAccount(ExpectedAccount + "2"));
                var provider = new Nt8HostDataProvider(masterMetadataReady: false);

                var observation = provider.GetSnapshot().AccountIdentity;

                Assert.Equal(1, observation.ObservedAccountCount);
                Assert.Equal(0, observation.MatchingAccountCount);
                Assert.Equal(DiagnosticStatus.FAIL, AccountIdentityVerifier.Evaluate(observation).Status);
            }
            finally
            {
                Reset();
            }
        }

        [Fact]
        public void Host_LowerCaseName_IsNotAnExactMatch()
        {
            Reset();
            try
            {
                Account.All.Add(IdentityAccount(ExpectedAccount.ToLowerInvariant()));
                var provider = new Nt8HostDataProvider(masterMetadataReady: false);

                var observation = provider.GetSnapshot().AccountIdentity;

                Assert.Equal(0, observation.MatchingAccountCount);
                Assert.Equal(DiagnosticStatus.FAIL, AccountIdentityVerifier.Evaluate(observation).Status);
            }
            finally
            {
                Reset();
            }
        }

        [Fact]
        public void Host_WrongConnectionName_IsMismatch()
        {
            Reset();
            try
            {
                var account = IdentityAccount(ExpectedAccount);
                account.Connection.Options.Name = "Other Rithmic";
                Account.All.Add(account);
                var provider = new Nt8HostDataProvider(masterMetadataReady: false);

                var observation = provider.GetSnapshot().AccountIdentity;

                Assert.Equal(1, observation.MatchingAccountCount);
                Assert.True(observation.ConnectionNameObservable);
                Assert.False(observation.ConnectionNameMatches);
                Assert.Equal(DiagnosticStatus.FAIL, AccountIdentityVerifier.Evaluate(observation).Status);
            }
            finally
            {
                Reset();
            }
        }

        [Fact]
        public void Host_LiveModeAndWrongCurrency_AreMismatches()
        {
            Reset();
            try
            {
                var account = IdentityAccount(ExpectedAccount);
                account.Connection.Options.Mode = Mode.Live;
                account.Denomination = Currency.Euro;
                Account.All.Add(account);
                var provider = new Nt8HostDataProvider(masterMetadataReady: false);

                var observation = provider.GetSnapshot().AccountIdentity;

                Assert.False(observation.ModeIsSimulation);
                Assert.False(observation.DenominationIsUsd);
                Assert.Equal(DiagnosticStatus.FAIL, AccountIdentityVerifier.Evaluate(observation).Status);
            }
            finally
            {
                Reset();
            }
        }

        [Fact]
        public void Host_NoConnection_LeavesIdentityInconclusive()
        {
            Reset();
            try
            {
                var account = IdentityAccount(ExpectedAccount);
                account.Connection = null;
                Account.All.Add(account);
                var provider = new Nt8HostDataProvider(masterMetadataReady: false);

                var observation = provider.GetSnapshot().AccountIdentity;

                Assert.Equal(1, observation.MatchingAccountCount);
                Assert.False(observation.ConnectionObserved);
                Assert.Equal(DiagnosticStatus.INCONCLUSIVE, AccountIdentityVerifier.Evaluate(observation).Status);
            }
            finally
            {
                Reset();
            }
        }

        [Fact]
        public void Host_AmbiguousExactMatches_AreInconclusive()
        {
            Reset();
            try
            {
                Account.All.Add(IdentityAccount(ExpectedAccount));
                Account.All.Add(IdentityAccount(ExpectedAccount));
                var provider = new Nt8HostDataProvider(masterMetadataReady: false);

                var observation = provider.GetSnapshot().AccountIdentity;

                Assert.Equal(2, observation.ObservedAccountCount);
                Assert.Equal(2, observation.MatchingAccountCount);
                Assert.Equal(DiagnosticStatus.INCONCLUSIVE, AccountIdentityVerifier.Evaluate(observation).Status);
            }
            finally
            {
                Reset();
            }
        }

        [Fact]
        public void Host_NoAccounts_LeavesIdentityInconclusive()
        {
            Reset();
            try
            {
                var provider = new Nt8HostDataProvider(masterMetadataReady: false);
                var observation = provider.GetSnapshot().AccountIdentity;

                Assert.True(observation.AccountSurfaceObservable);
                Assert.Equal(0, observation.ObservedAccountCount);
                Assert.Equal(DiagnosticStatus.INCONCLUSIVE, AccountIdentityVerifier.Evaluate(observation).Status);
            }
            finally
            {
                Reset();
            }
        }

        [Fact]
        public void Host_Observation_CarriesNoRawIdentity()
        {
            Reset();
            try
            {
                Account.All.Add(IdentityAccount(ExpectedAccount));
                var provider = new Nt8HostDataProvider(masterMetadataReady: false);

                var observation = provider.GetSnapshot().AccountIdentity;

                // The observation is booleans/counts plus a synthetic ref only; the raw account
                // and connection names never appear in it.
                var text = observation.AccountRef ?? string.Empty;
                Assert.StartsWith("account#", text);
                Assert.DoesNotContain(ExpectedAccount, text);
            }
            finally
            {
                Reset();
            }
        }
    }
}
