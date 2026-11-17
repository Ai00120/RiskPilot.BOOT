using System;
using System.Collections.Generic;
using NinjaTrader.Cbi;
using RiskPilot.Diagnostics.Nt8.Core.Engine;
using RiskPilot.Diagnostics.Nt8.Core.Engine.Probes;
using RiskPilot.Diagnostics.Nt8.Core.Evidence;
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

        // ---------------- pure chain classifier ----------------

        [Fact]
        public void ChainClassifier_AllLinksVerified_Simulation_IsSimulation()
        {
            Assert.Equal(EnvironmentClass.Simulation,
                EnvironmentClassifier.ClassifyVerifiedChain(true, true, true, true));
        }

        [Fact]
        public void ChainClassifier_AllLinksVerified_Live_IsLive()
        {
            Assert.Equal(EnvironmentClass.Live,
                EnvironmentClassifier.ClassifyVerifiedChain(true, true, true, false));
        }

        [Fact]
        public void ChainClassifier_AccountUnverified_IsUnknown()
        {
            Assert.Equal(EnvironmentClass.Unknown,
                EnvironmentClassifier.ClassifyVerifiedChain(false, true, true, true));
        }

        [Fact]
        public void ChainClassifier_ConnectionUnverified_IsUnknown()
        {
            Assert.Equal(EnvironmentClass.Unknown,
                EnvironmentClassifier.ClassifyVerifiedChain(true, false, true, true));
        }

        [Fact]
        public void ChainClassifier_ModeUnobservable_IsUnknown()
        {
            Assert.Equal(EnvironmentClass.Unknown,
                EnvironmentClassifier.ClassifyVerifiedChain(true, true, false, true));
        }

        // ---------------- host adapter: association is part of the chain ----------------

        /// <summary>
        /// A target account whose connection state is not connected: the identity verifier still
        /// treats the connection status as unobservable-for-verification, so the association cannot
        /// be proven and the environment must stay Unknown (never a class from a connected
        /// market-data feed alone).
        /// </summary>
        private static Connection DisconnectedRequiredConnection(Mode mode)
        {
            return new Connection
            {
                Status = ConnectionStatus.Disconnected,
                PriceStatus = ConnectionStatus.Connected,
                Options = new ConnectOptions
                {
                    Name = ExpectedConnection,
                    Provider = Provider.Provider14,
                    Mode = mode
                }
            };
        }

        [Fact]
        public void Host_DisconnectedTargetConnection_EnvironmentIsUnknown()
        {
            Reset();
            try
            {
                var connection = DisconnectedRequiredConnection(Mode.Simulation);
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
        public void Host_AmbiguousAccountMatches_EnvironmentIsUnknown()
        {
            Reset();
            try
            {
                // Two accounts match the target name exactly, so the target cannot be uniquely
                // identified. The environment class must be Unknown (INCONCLUSIVE), never the
                // class of one of the ambiguous accounts.
                Account.All.Add(TargetAccount(RequiredConnection(Mode.Simulation)));
                Account.All.Add(TargetAccount(RequiredConnection(Mode.Simulation)));
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
        public void Host_AmbiguousConnections_EnvironmentIsUnknown()
        {
            Reset();
            try
            {
                // Two enumerated connections match the required connection identity, so the
                // association is ambiguous and the class must stay Unknown.
                var target = RequiredConnection(Mode.Simulation);
                Account.All.Add(TargetAccount(target));
                Connection.Connections.Add(target);
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
        public void Host_AccountBoundToDifferentConnection_EnvironmentIsUnknown()
        {
            Reset();
            try
            {
                // The account is bound to a different connection (wrong name) while a required
                // connection is also present. The association fails, so no class is established.
                var wrong = RequiredConnection(Mode.Simulation);
                wrong.Options.Name = "Other Rithmic";
                Account.All.Add(TargetAccount(wrong));
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
        public void Host_ConnectionNotEnumerated_EnvironmentIsUnknown()
        {
            Reset();
            try
            {
                // The account reports its own connection, but the host does not enumerate it in
                // Connection.Connections, so the association is unproven.
                Account.All.Add(TargetAccount(RequiredConnection(Mode.Simulation)));
                // No Connection.Connections entries at all.

                var provider = TestHost.Provider(true);
                var environment = provider.GetEnvironment();

                Assert.Equal(EnvironmentClass.Unknown, environment.Environment);
            }
            finally
            {
                Reset();
            }
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Host_EnumerationOrder_NeverChangesTheEnvironmentResult(bool targetFirst)
        {
            Reset();
            try
            {
                // The verified target is Simulation while an unrelated "Sim"-named Live account is
                // also present. Whatever the enumeration order, the class must be Simulation
                // (driven only by the verified target), never the first account.
                var unrelated = new Account
                {
                    Name = "SimOther",
                    Provider = Provider.Provider14,
                    Denomination = Currency.UsDollar,
                    Connection = RequiredConnection(Mode.Live)
                };
                var targetConnection = RequiredConnection(Mode.Simulation);
                var target = TargetAccount(targetConnection);

                if (targetFirst)
                {
                    Account.All.Add(target);
                    Account.All.Add(unrelated);
                }
                else
                {
                    Account.All.Add(unrelated);
                    Account.All.Add(target);
                }

                Connection.Connections.Add(targetConnection);

                var provider = TestHost.Provider(true);
                Assert.Equal(EnvironmentClass.Simulation, provider.GetEnvironment().Environment);
            }
            finally
            {
                Reset();
            }
        }

        // ---------------- verification chain evidence ----------------

        private static AccountIdentityObservation VerifiedAccountIdentity()
        {
            return new AccountIdentityObservation
            {
                AccountSurfaceObservable = true,
                ObservedAccountCount = 1,
                MatchingAccountCount = 1,
                AccountRef = "account#chain",
                AccountProviderObservable = true,
                AccountProviderMatch = ProviderIdentityMatch.Matches,
                ConnectionObserved = true,
                ConnectionNameObservable = true,
                ConnectionNameMatches = true,
                ConnectionProviderObservable = true,
                ConnectionProviderMatch = ProviderIdentityMatch.Matches,
                ConnectionStatusObservable = true,
                ConnectionConnected = true,
                ModeObservable = true,
                ModeIsSimulation = true,
                DenominationObservable = true,
                DenominationIsUsd = true
            };
        }

        private static TargetAccountConnectionObservation VerifiedAssociation()
        {
            return new TargetAccountConnectionObservation
            {
                AccountSurfaceObservable = true,
                ObservedAccountCount = 1,
                MatchingAccountCount = 1,
                ConnectionCount = 1,
                MatchingConnectionCount = 1,
                AccountRef = "account#chain",
                AccountProviderObservable = true,
                AccountProviderMatch = ProviderIdentityMatch.Matches,
                ConnectionObserved = true,
                ConnectionEnumerated = true,
                ConnectionNameObservable = true,
                ConnectionNameMatches = true,
                ConnectionProviderObservable = true,
                ConnectionProviderMatch = ProviderIdentityMatch.Matches
            };
        }

        [Fact]
        public void Chain_FullyVerified_IsFullyVerified()
        {
            var chain = EnvironmentVerificationChain.Evaluate(
                VerifiedAccountIdentity(), VerifiedAssociation(), EnvironmentClass.Simulation);

            Assert.True(chain.AccountVerified);
            Assert.True(chain.ConnectionVerified);
            Assert.True(chain.ProviderVerified);
            Assert.True(chain.EnvironmentEstablished);
            Assert.True(chain.FullyVerified);
            Assert.Contains(EnvironmentVerificationChain.Verified, chain.ReasonCodes);
        }

        [Fact]
        public void Chain_UnverifiedAccount_IsNotFullyVerified()
        {
            var chain = EnvironmentVerificationChain.Evaluate(
                null, VerifiedAssociation(), EnvironmentClass.Simulation);

            Assert.False(chain.AccountVerified);
            Assert.False(chain.FullyVerified);
            Assert.Contains(EnvironmentVerificationChain.AccountUnverified, chain.ReasonCodes);
        }

        [Fact]
        public void Chain_UnverifiedAssociation_IsNotFullyVerified()
        {
            var chain = EnvironmentVerificationChain.Evaluate(
                VerifiedAccountIdentity(), null, EnvironmentClass.Simulation);

            Assert.False(chain.ConnectionVerified);
            Assert.False(chain.FullyVerified);
            Assert.Contains(EnvironmentVerificationChain.ConnectionUnverified, chain.ReasonCodes);
        }

        [Fact]
        public void Chain_UnresolvedProvider_IsNotFullyVerified()
        {
            var identity = VerifiedAccountIdentity();
            identity.AccountProviderMatch = ProviderIdentityMatch.Unresolved;
            var chain = EnvironmentVerificationChain.Evaluate(
                identity, VerifiedAssociation(), EnvironmentClass.Simulation);

            Assert.False(chain.ProviderVerified);
            Assert.False(chain.FullyVerified);
            Assert.Contains(EnvironmentVerificationChain.ProviderUnverified, chain.ReasonCodes);
        }

        [Fact]
        public void Chain_UnknownEnvironment_IsNotFullyVerified()
        {
            var chain = EnvironmentVerificationChain.Evaluate(
                VerifiedAccountIdentity(), VerifiedAssociation(), EnvironmentClass.Unknown);

            Assert.False(chain.EnvironmentEstablished);
            Assert.False(chain.FullyVerified);
            Assert.Contains(EnvironmentVerificationChain.EnvironmentUnknown, chain.ReasonCodes);
        }

        [Fact]
        public void M1_EnvironmentClass_PassesOnlyWithFullyVerifiedChain()
        {
            var provider = new FakeDataProvider
            {
                Environment = new EnvironmentRecord { Environment = EnvironmentClass.Simulation },
                Snapshot = new DiagnosticSnapshot
                {
                    AccountIdentity = VerifiedAccountIdentity(),
                    TargetAccountConnection = VerifiedAssociation()
                }
            };

            var results = new M1EnvironmentProbe().Evaluate(
                new ProbeContext(
                    provider,
                    new SessionContext(SessionId.New(DateTime.UtcNow), DateTime.UtcNow),
                    new FakeClock(DateTime.UtcNow),
                    "corr00000000"));

            DiagnosticResult found = null;
            foreach (var result in results)
            {
                if (result.CheckId == "M1.ENVIRONMENT_CLASS")
                {
                    found = result;
                }
            }

            Assert.NotNull(found);
            Assert.Equal(DiagnosticStatus.PASS, found.Status);
            Assert.Contains(EnvironmentVerificationChain.Verified, found.ReasonCodes);
        }

        [Fact]
        public void M1_EnvironmentClass_StaysInconclusive_WhenAssociationUnresolved()
        {
            var provider = new FakeDataProvider
            {
                Environment = new EnvironmentRecord { Environment = EnvironmentClass.Simulation },
                Snapshot = new DiagnosticSnapshot
                {
                    AccountIdentity = VerifiedAccountIdentity(),
                    TargetAccountConnection = null
                }
            };

            var results = new M1EnvironmentProbe().Evaluate(
                new ProbeContext(
                    provider,
                    new SessionContext(SessionId.New(DateTime.UtcNow), DateTime.UtcNow),
                    new FakeClock(DateTime.UtcNow),
                    "corr00000000"));

            DiagnosticResult found = null;
            foreach (var result in results)
            {
                if (result.CheckId == "M1.ENVIRONMENT_CLASS")
                {
                    found = result;
                }
            }

            Assert.NotNull(found);
            Assert.Equal(DiagnosticStatus.INCONCLUSIVE, found.Status);
            Assert.Contains(EnvironmentVerificationChain.ConnectionUnverified, found.ReasonCodes);
        }

        [Fact]
        public void EnvironmentJson_CarriesVerificationChainBlock()
        {
            var dataset = new EvidenceDataset
            {
                Environment = new EnvironmentRecord { Environment = EnvironmentClass.Simulation },
                Snapshot = new DiagnosticSnapshot
                {
                    AccountIdentity = VerifiedAccountIdentity(),
                    TargetAccountConnection = VerifiedAssociation()
                }
            };

            var package = EvidencePackageBuilder.Build(null, dataset);
            var environment = package.Read(EvidencePackageCatalog.EnvironmentJson);

            Assert.Contains("\"environmentVerificationChain\"", environment);
            Assert.Contains("\"verified\":true", environment);
            Assert.Contains("\"targetAccount\":true", environment);
            Assert.Contains("\"connectionAssociation\":true", environment);
            Assert.Contains("\"providerIdentity\":true", environment);
            Assert.Contains(EnvironmentVerificationChain.Verified, environment);
        }

        [Fact]
        public void EnvironmentJson_ChainUnresolved_NeverReportsVerifiedEnvironment()
        {
            var dataset = new EvidenceDataset
            {
                // A class is present on the record, but no identity/association evidence backs it,
                // so the chain is unresolved and the class must be written UNKNOWN / verified:false.
                Environment = new EnvironmentRecord { Environment = EnvironmentClass.Simulation },
                Snapshot = new DiagnosticSnapshot()
            };

            var package = EvidencePackageBuilder.Build(null, dataset);
            var environment = package.Read(EvidencePackageCatalog.EnvironmentJson);

            Assert.Contains("\"verified\":false", environment);
            Assert.Contains("\"verifiedEnvironmentClass\":\"UNKNOWN\"", environment);
            Assert.Contains(EnvironmentVerificationChain.AccountUnverified, environment);
            Assert.Contains(EnvironmentVerificationChain.ConnectionUnverified, environment);
        }
    }
}
