using NinjaTrader.Cbi;
using RiskPilot.Diagnostics.Nt8.Core.Identity;
using RiskPilot.Diagnostics.Nt8.Core.Models.Observations;
using RiskPilot.Diagnostics.Nt8.Core.Status;
using RiskPilot.Diagnostics.Nt8.Host;
using Xunit;

namespace RiskPilot.Diagnostics.Nt8.Tests
{
    /// <summary>
    /// Tests for the host-layer target-account→connection→provider adapter over the OFFLINE
    /// NinjaTrader stub surface. They exercise the real <see cref="Nt8HostDataProvider"/> mapping
    /// code (resolution of the target account's own connection, connection enumeration and
    /// provider attribution) so a regression in the adapter is caught offline. This validates the
    /// adapter against the stub surface only; it is NOT proof of real host behaviour.
    /// </summary>
    public class HostTargetAccountConnectionTests
    {
        private const string ExpectedAccount = "SimRP.BOT";
        private const string ExpectedConnection = "My Rithmic";

        private static void Reset()
        {
            Account.All.Clear();
            Connection.Connections.Clear();
        }

        private static Connection RequiredConnection(Provider provider = Provider.Rithmic, string name = ExpectedConnection)
        {
            return new Connection
            {
                Status = ConnectionStatus.Connected,
                PriceStatus = ConnectionStatus.Connected,
                Options = new ConnectOptions
                {
                    Name = name,
                    Provider = provider,
                    Mode = Mode.Simulation
                }
            };
        }

        private static Account TargetAccount(Connection connection, Provider accountProvider = Provider.Rithmic)
        {
            return new Account
            {
                Name = ExpectedAccount,
                Provider = accountProvider,
                Denomination = Currency.UsDollar,
                Connection = connection
            };
        }

        [Fact]
        public void Host_ExactAssociation_ProducesPassingObservation()
        {
            Reset();
            try
            {
                var connection = RequiredConnection();
                Account.All.Add(TargetAccount(connection));
                Connection.Connections.Add(connection);

                var provider = new Nt8HostDataProvider(masterMetadataReady: false);
                var observation = provider.GetSnapshot().TargetAccountConnection;

                Assert.True(observation.AccountSurfaceObservable);
                Assert.Equal(1, observation.ObservedAccountCount);
                Assert.Equal(1, observation.MatchingAccountCount);
                Assert.Equal(1, observation.ConnectionCount);
                Assert.Equal(1, observation.MatchingConnectionCount);
                Assert.True(observation.AccountProviderMatches);
                Assert.True(observation.ConnectionObserved);
                Assert.True(observation.ConnectionNameMatches);
                Assert.True(observation.ConnectionProviderMatches);
                Assert.Equal(DiagnosticStatus.PASS, TargetAccountConnectionVerifier.Evaluate(observation).Status);
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
                Account.All.Add(TargetAccount(RequiredConnection(name: "Other Rithmic")));

                var provider = new Nt8HostDataProvider(masterMetadataReady: false);
                var observation = provider.GetSnapshot().TargetAccountConnection;

                Assert.True(observation.ConnectionNameObservable);
                Assert.False(observation.ConnectionNameMatches);
                Assert.Equal(DiagnosticStatus.FAIL, TargetAccountConnectionVerifier.Evaluate(observation).Status);
            }
            finally
            {
                Reset();
            }
        }

        [Fact]
        public void Host_WrongProvider_IsMismatch()
        {
            Reset();
            try
            {
                Account.All.Add(TargetAccount(RequiredConnection(provider: Provider.Simulator)));

                var provider = new Nt8HostDataProvider(masterMetadataReady: false);
                var observation = provider.GetSnapshot().TargetAccountConnection;

                Assert.False(observation.ConnectionProviderMatches);
                Assert.Equal(DiagnosticStatus.FAIL, TargetAccountConnectionVerifier.Evaluate(observation).Status);
            }
            finally
            {
                Reset();
            }
        }

        [Fact]
        public void Host_NoConnection_IsInconclusive()
        {
            Reset();
            try
            {
                Account.All.Add(TargetAccount(null));

                var provider = new Nt8HostDataProvider(masterMetadataReady: false);
                var observation = provider.GetSnapshot().TargetAccountConnection;

                Assert.False(observation.ConnectionObserved);
                Assert.Equal(DiagnosticStatus.INCONCLUSIVE, TargetAccountConnectionVerifier.Evaluate(observation).Status);
            }
            finally
            {
                Reset();
            }
        }

        [Fact]
        public void Host_UnrelatedConnectionPresent_DoesNotChangeTheTargetAssociation()
        {
            Reset();
            try
            {
                var target = RequiredConnection();
                Account.All.Add(TargetAccount(target));

                // An unrelated connection (e.g. a data feed) is enumerated too, but the target
                // account's own connection is the only one that proves the association.
                Connection.Connections.Add(new Connection
                {
                    Status = ConnectionStatus.Connected,
                    PriceStatus = ConnectionStatus.Connected,
                    Options = new ConnectOptions { Name = "Kinetick", Provider = Provider.Default, Mode = Mode.Live }
                });
                Connection.Connections.Add(target);

                var provider = new Nt8HostDataProvider(masterMetadataReady: false);
                var observation = provider.GetSnapshot().TargetAccountConnection;

                Assert.Equal(2, observation.ConnectionCount);
                Assert.Equal(1, observation.MatchingConnectionCount);
                Assert.True(observation.ConnectionNameMatches);
                Assert.Equal(DiagnosticStatus.PASS, TargetAccountConnectionVerifier.Evaluate(observation).Status);
            }
            finally
            {
                Reset();
            }
        }

        [Fact]
        public void Host_TwoMatchingConnections_IsAmbiguousInconclusive()
        {
            Reset();
            try
            {
                var target = RequiredConnection();
                Account.All.Add(TargetAccount(target));

                // A second connection also matches the required identity, so the host cannot prove
                // which one the target account is bound to.
                Connection.Connections.Add(target);
                Connection.Connections.Add(RequiredConnection());

                var provider = new Nt8HostDataProvider(masterMetadataReady: false);
                var observation = provider.GetSnapshot().TargetAccountConnection;

                Assert.Equal(2, observation.ConnectionCount);
                Assert.Equal(2, observation.MatchingConnectionCount);
                var verdict = TargetAccountConnectionVerifier.Evaluate(observation);
                Assert.Equal(DiagnosticStatus.INCONCLUSIVE, verdict.Status);
                Assert.Contains(TargetAccountConnectionReasonCodes.AmbiguousConnection, verdict.ReasonCodes);
            }
            finally
            {
                Reset();
            }
        }

        [Fact]
        public void Host_AmbiguousAccount_IsInconclusive()
        {
            Reset();
            try
            {
                Account.All.Add(TargetAccount(RequiredConnection()));
                Account.All.Add(TargetAccount(RequiredConnection()));

                var provider = new Nt8HostDataProvider(masterMetadataReady: false);
                var observation = provider.GetSnapshot().TargetAccountConnection;

                Assert.Equal(2, observation.MatchingAccountCount);
                Assert.Equal(DiagnosticStatus.INCONCLUSIVE, TargetAccountConnectionVerifier.Evaluate(observation).Status);
            }
            finally
            {
                Reset();
            }
        }

        [Fact]
        public void Host_NoAccounts_IsInconclusive()
        {
            Reset();
            try
            {
                var provider = new Nt8HostDataProvider(masterMetadataReady: false);
                var observation = provider.GetSnapshot().TargetAccountConnection;

                Assert.True(observation.AccountSurfaceObservable);
                Assert.Equal(0, observation.ObservedAccountCount);
                Assert.Equal(DiagnosticStatus.INCONCLUSIVE, TargetAccountConnectionVerifier.Evaluate(observation).Status);
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
                Account.All.Add(TargetAccount(RequiredConnection()));
                var provider = new Nt8HostDataProvider(masterMetadataReady: false);
                var observation = provider.GetSnapshot().TargetAccountConnection;

                var text = observation.AccountRef ?? string.Empty;
                Assert.StartsWith("account#", text);
                Assert.DoesNotContain(ExpectedAccount, text);
            }
            finally
            {
                Reset();
            }
        }

        // ---------------- provider attribution ----------------

        [Fact]
        public void Host_ProviderResolution_UsesTargetAccountConnection_NotFirstConnection()
        {
            Reset();
            try
            {
                var target = RequiredConnection(Provider.Rithmic);
                Account.All.Add(TargetAccount(target));

                // The first enumerated connection belongs to an unrelated provider; the provider
                // must be read from the target account's own connection, not the first one.
                Connection.Connections.Add(RequiredConnection(Provider.Simulator, "Unrelated"));
                Connection.Connections.Add(target);

                var provider = new Nt8HostDataProvider(masterMetadataReady: false);
                var evidence = provider.GetSnapshot().Provider;
                var record = provider.GetProvider();

                Assert.NotNull(evidence);
                Assert.Equal("Rithmic", evidence.RawProviderValue);
                Assert.Equal(ProviderResolutionBasis.TargetAccountConnection, evidence.ResolutionBasis);
                Assert.NotNull(record);
                Assert.Equal("Rithmic", record.Name);
                Assert.True(record.IsExpectedEvidenceProvider);
            }
            finally
            {
                Reset();
            }
        }

        [Fact]
        public void Host_ProviderResolution_MultipleConnectionsWithoutTargetAccount_IsNotAttributed()
        {
            Reset();
            try
            {
                // No target account, and two connections: the provider cannot be attributed to
                // an unambiguous connection, so it must not be taken from the first one.
                Connection.Connections.Add(RequiredConnection(Provider.Rithmic));
                Connection.Connections.Add(RequiredConnection(Provider.Simulator, "Unrelated"));

                var provider = new Nt8HostDataProvider(masterMetadataReady: false);

                Assert.Null(provider.GetProvider());
                Assert.Null(provider.GetSnapshot().Provider);
            }
            finally
            {
                Reset();
            }
        }

        [Fact]
        public void Host_ProviderResolution_SingleConnectionWithoutTargetAccount_IsAttributed()
        {
            Reset();
            try
            {
                Connection.Connections.Add(RequiredConnection(Provider.Rithmic));

                var provider = new Nt8HostDataProvider(masterMetadataReady: false);
                var evidence = provider.GetSnapshot().Provider;

                Assert.NotNull(evidence);
                Assert.Equal("Rithmic", evidence.RawProviderValue);
                Assert.Equal(ProviderResolutionBasis.SingleConnection, evidence.ResolutionBasis);
            }
            finally
            {
                Reset();
            }
        }
    }
}
