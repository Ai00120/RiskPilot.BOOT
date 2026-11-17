using NinjaTrader.Cbi;
using RiskPilot.Diagnostics.Nt8.Core.Identity;
using RiskPilot.Diagnostics.Nt8.Core.Models;
using RiskPilot.Diagnostics.Nt8.Host;
using Xunit;

namespace RiskPilot.Diagnostics.Nt8.Tests
{
    /// <summary>
    /// Tests for the verified-account environment classification. The environment class is derived
    /// from the M2-verified target account's <em>own</em> connection mode, never from the first
    /// account enumerated in <c>Account.All</c> and never from an account-name convention. They
    /// drive the real <see cref="EnvironmentClassifier"/> and the real
    /// <see cref="Nt8HostDataProvider"/> over the OFFLINE NinjaTrader stub surface; this is NOT
    /// proof of real host behaviour.
    /// </summary>
    [Collection("StubHostState")]
    public class EnvironmentClassificationTests
    {
        private const string ExpectedAccount = "SimRP.BOT";
        private const string ExpectedConnection = "My Rithmic";

        private static void Reset()
        {
            Account.All.Clear();
            Connection.Connections.Clear();
        }

        private static Connection RequiredConnection(Mode mode)
        {
            return new Connection
            {
                Status = ConnectionStatus.Connected,
                PriceStatus = ConnectionStatus.Connected,
                Options = new ConnectOptions
                {
                    Name = ExpectedConnection,
                    Provider = Provider.Provider14,
                    Mode = mode
                }
            };
        }

        private static Account TargetAccount(Connection connection)
        {
            return new Account
            {
                Name = ExpectedAccount,
                Provider = Provider.Provider14,
                Denomination = Currency.UsDollar,
                Connection = connection
            };
        }

        // ---------------- pure classifier ----------------

        [Fact]
        public void Classifier_VerifiedTarget_SimulationMode_IsSimulation()
        {
            Assert.Equal(EnvironmentClass.Simulation, EnvironmentClassifier.Classify(true, true, true));
        }

        [Fact]
        public void Classifier_VerifiedTarget_LiveMode_IsLive()
        {
            Assert.Equal(EnvironmentClass.Live, EnvironmentClassifier.Classify(true, true, false));
        }

        [Fact]
        public void Classifier_NoVerifiedTarget_IsUnknown()
        {
            Assert.Equal(EnvironmentClass.Unknown, EnvironmentClassifier.Classify(false, true, true));
        }

        [Fact]
        public void Classifier_UnobservableMode_IsUnknown()
        {
            Assert.Equal(EnvironmentClass.Unknown, EnvironmentClassifier.Classify(true, false, true));
        }

        // ---------------- host adapter ----------------

        [Fact]
        public void Host_VerifiedTarget_SimulationMode_IsSimulation()
        {
            Reset();
            try
            {
                var connection = RequiredConnection(Mode.Simulation);
                Account.All.Add(TargetAccount(connection));
                Connection.Connections.Add(connection);

                var provider = TestHost.Provider(true);
                var environment = provider.GetEnvironment();

                Assert.Equal(EnvironmentClass.Simulation, environment.Environment);
            }
            finally
            {
                Reset();
            }
        }

        [Fact]
        public void Host_UnverifiedTarget_LiveMode_IsUnknown_NeverLive()
        {
            Reset();
            try
            {
                // A target account whose connection mode is Live cannot be an M2-verified target
                // (the approved identity requires Simulation mode). The environment class must then
                // be Unknown (fail closed), never a fabricated Live.
                var connection = RequiredConnection(Mode.Live);
                Account.All.Add(TargetAccount(connection));
                Connection.Connections.Add(connection);

                var provider = TestHost.Provider(true);
                var environment = provider.GetEnvironment();

                Assert.Equal(EnvironmentClass.Unknown, environment.Environment);
            }
            finally
            {
                Reset();
            }
        }

        [Fact]
        public void Host_NoVerifiedTarget_IsUnknown_NeverFromFirstAccount()
        {
            Reset();
            try
            {
                // An unrelated account whose name happens to start with "Sim" must NOT drive the
                // environment class: with no verified target the class is Unknown (INCONCLUSIVE).
                Connection.Connections.Add(RequiredConnection(Mode.Simulation));
                Account.All.Add(new Account
                {
                    Name = "SimOther",
                    Provider = Provider.Provider14,
                    Denomination = Currency.UsDollar,
                    Connection = RequiredConnection(Mode.Simulation)
                });

                var provider = TestHost.Provider(true);
                var environment = provider.GetEnvironment();

                Assert.Equal(EnvironmentClass.Unknown, environment.Environment);
            }
            finally
            {
                Reset();
            }
        }

        [Fact]
        public void Host_NoAccountsAtAll_IsUnknown()
        {
            Reset();
            try
            {
                var provider = TestHost.Provider(true);
                var environment = provider.GetEnvironment();

                Assert.Equal(EnvironmentClass.Unknown, environment.Environment);
            }
            finally
            {
                Reset();
            }
        }

        [Fact]
        public void Host_VerifiedTargetWithoutConnection_IsUnknown()
        {
            Reset();
            try
            {
                var account = TargetAccount(RequiredConnection(Mode.Simulation));
                account.Connection = null;
                Account.All.Add(account);
                Connection.Connections.Add(RequiredConnection(Mode.Simulation));

                var provider = TestHost.Provider(true);
                var environment = provider.GetEnvironment();

                Assert.Equal(EnvironmentClass.Unknown, environment.Environment);
            }
            finally
            {
                Reset();
            }
        }

        [Fact]
        public void Host_AccountRecordEnvironmentClass_MatchesVerifiedTargetMode()
        {
            Reset();
            try
            {
                var connection = RequiredConnection(Mode.Simulation);
                Account.All.Add(TargetAccount(connection));
                Connection.Connections.Add(connection);

                var provider = TestHost.Provider(true);
                var accounts = provider.GetAccounts();

                Assert.Single(accounts);
                Assert.Equal(EnvironmentClass.Simulation, accounts[0].AccountClass);
            }
            finally
            {
                Reset();
            }
        }

        [Fact]
        public void Host_FirstAccountNotTarget_EnvironmentStillFollowsVerifiedTarget()
        {
            Reset();
            try
            {
                // A non-target account is enumerated first (a Live-mode "Sim"-named account). The
                // verified target (Simulation) must still drive the class, proving the first
                // account is never consulted.
                Account.All.Add(new Account
                {
                    Name = "SimOther",
                    Provider = Provider.Provider14,
                    Denomination = Currency.UsDollar,
                    Connection = RequiredConnection(Mode.Live)
                });

                var targetConnection = RequiredConnection(Mode.Simulation);
                Account.All.Add(TargetAccount(targetConnection));
                Connection.Connections.Add(targetConnection);

                var provider = TestHost.Provider(true);
                var environment = provider.GetEnvironment();

                Assert.Equal(EnvironmentClass.Simulation, environment.Environment);
            }
            finally
            {
                Reset();
            }
        }
    }
}
