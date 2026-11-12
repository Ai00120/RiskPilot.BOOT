using System;
using System.Collections.Generic;
using RiskPilot.Diagnostics.Nt8.Core.Engine;
using RiskPilot.Diagnostics.Nt8.Core.Engine.Probes;
using RiskPilot.Diagnostics.Nt8.Core.Identity;
using RiskPilot.Diagnostics.Nt8.Core.Models;
using RiskPilot.Diagnostics.Nt8.Core.Models.Observations;
using RiskPilot.Diagnostics.Nt8.Core.Result;
using RiskPilot.Diagnostics.Nt8.Core.Security;
using RiskPilot.Diagnostics.Nt8.Core.Session;
using RiskPilot.Diagnostics.Nt8.Core.Status;
using RiskPilot.Diagnostics.Nt8.Tests.Fakes;
using Xunit;

namespace RiskPilot.Diagnostics.Nt8.Tests
{
    /// <summary>
    /// Regression tests for the exact account-to-connection and provider association required by
    /// RiskPilot.BOT (D-095/D-096): the target account must be bound to connection "My Rithmic"
    /// with provider "Rithmic", proven from the account's own connection rather than an arbitrary
    /// or first connection. They drive the real verifier and the real M2 probe and assert the
    /// fail-closed rules: a wrong account, a wrong/unrelated connection, a wrong provider, a
    /// missing connection and an ambiguous association are never a PASS, and no raw account or
    /// connection name is ever emitted.
    /// </summary>
    public class TargetAccountConnectionTests
    {
        private const string ExpectedAccount = "SimRP.BOT";
        private const string ExpectedConnection = "My Rithmic";
        private const string ExpectedProvider = "Rithmic";

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

        /// <summary>
        /// A fully observed, fully matching association of the exact expected target account to
        /// its required connection. Each test perturbs exactly one field so the failure mode is
        /// unambiguous.
        /// </summary>
        private static TargetAccountConnectionObservation ExactAssociation()
        {
            return new TargetAccountConnectionObservation
            {
                AccountSurfaceObservable = true,
                ObservedAccountCount = 1,
                MatchingAccountCount = 1,
                ConnectionCount = 1,
                MatchingConnectionCount = 1,
                AccountRef = SyntheticRef.For("account", ExpectedAccount),
                AccountProviderObservable = true,
                AccountProviderMatches = true,
                ConnectionObserved = true,
                ConnectionNameObservable = true,
                ConnectionNameMatches = true,
                ConnectionProviderObservable = true,
                ConnectionProviderMatches = true
            };
        }

        private static FakeDataProvider ProviderWithSnapshot(TargetAccountConnectionObservation observation)
        {
            var provider = new FakeDataProvider
            {
                Snapshot = new DiagnosticSnapshot { TargetAccountConnection = observation }
            };
            provider.Connections.Add(new ConnectionRecord
            {
                ConnectionName = ExpectedConnection,
                ProviderName = ExpectedProvider,
                PriceState = ConnectionState.Connected,
                OrderChannelState = ConnectionState.Connected,
                Role = ConnectionRole.Broker
            });
            return provider;
        }

        // ---------------- verifier: exact match ----------------

        [Fact]
        public void Verifier_ExactAssociation_IsPass()
        {
            var verdict = TargetAccountConnectionVerifier.Evaluate(ExactAssociation());
            Assert.Equal(DiagnosticStatus.PASS, verdict.Status);
            Assert.Contains(TargetAccountConnectionReasonCodes.Match, verdict.ReasonCodes);
            Assert.Empty(verdict.MismatchedFields);
            Assert.Empty(verdict.UnobservableFields);
        }

        // ---------------- verifier: wrong target account ----------------

        [Fact]
        public void Verifier_NoAccountNameMatch_IsFail()
        {
            var observation = ExactAssociation();
            observation.MatchingAccountCount = 0;
            var verdict = TargetAccountConnectionVerifier.Evaluate(observation);

            Assert.Equal(DiagnosticStatus.FAIL, verdict.Status);
            Assert.Contains(TargetAccountConnectionReasonCodes.NameMismatch, verdict.ReasonCodes);
        }

        [Fact]
        public void Verifier_MissingAccount_IsInconclusive()
        {
            var observation = new TargetAccountConnectionObservation
            {
                AccountSurfaceObservable = true,
                ObservedAccountCount = 0,
                MatchingAccountCount = 0
            };
            var verdict = TargetAccountConnectionVerifier.Evaluate(observation);

            Assert.Equal(DiagnosticStatus.INCONCLUSIVE, verdict.Status);
            Assert.Contains(TargetAccountConnectionReasonCodes.NoAccountObserved, verdict.ReasonCodes);
        }

        [Fact]
        public void Verifier_UnobservableSurface_IsInconclusive()
        {
            var verdict = TargetAccountConnectionVerifier.Evaluate(new TargetAccountConnectionObservation());
            Assert.Equal(DiagnosticStatus.INCONCLUSIVE, verdict.Status);
            Assert.Contains(TargetAccountConnectionReasonCodes.Unobservable, verdict.ReasonCodes);
        }

        [Fact]
        public void Verifier_StubOrMockValue_IsInconclusive_NeverPass()
        {
            var observation = ExactAssociation();
            observation.IsStubOrMockValue = true;
            var verdict = TargetAccountConnectionVerifier.Evaluate(observation);

            Assert.Equal(DiagnosticStatus.INCONCLUSIVE, verdict.Status);
            Assert.Contains(TargetAccountConnectionReasonCodes.StubOrMock, verdict.ReasonCodes);
        }

        // ---------------- verifier: multiple / ambiguous ----------------

        [Fact]
        public void Verifier_AmbiguousAccountMatch_IsInconclusive()
        {
            var observation = ExactAssociation();
            observation.ObservedAccountCount = 2;
            observation.MatchingAccountCount = 2;
            var verdict = TargetAccountConnectionVerifier.Evaluate(observation);

            Assert.Equal(DiagnosticStatus.INCONCLUSIVE, verdict.Status);
            Assert.Contains(TargetAccountConnectionReasonCodes.AmbiguousAccount, verdict.ReasonCodes);
        }

        [Fact]
        public void Verifier_MultipleMatchingConnections_IsInconclusive()
        {
            // Two enumerated connections both match the required connection identity, so the
            // host cannot prove which one the target account is bound to.
            var observation = ExactAssociation();
            observation.ConnectionCount = 2;
            observation.MatchingConnectionCount = 2;
            var verdict = TargetAccountConnectionVerifier.Evaluate(observation);

            Assert.Equal(DiagnosticStatus.INCONCLUSIVE, verdict.Status);
            Assert.Contains(TargetAccountConnectionReasonCodes.AmbiguousConnection, verdict.ReasonCodes);
        }

        // ---------------- verifier: missing / wrong connection ----------------

        [Fact]
        public void Verifier_NoConnectionObserved_IsInconclusive()
        {
            var observation = ExactAssociation();
            observation.ConnectionObserved = false;
            var verdict = TargetAccountConnectionVerifier.Evaluate(observation);

            Assert.Equal(DiagnosticStatus.INCONCLUSIVE, verdict.Status);
            Assert.Contains(TargetAccountConnectionReasonCodes.ConnectionNotObserved, verdict.ReasonCodes);
            Assert.Contains(TargetAccountConnectionReasonCodes.ConnectionNotObserved, verdict.UnobservableFields);
        }

        [Fact]
        public void Verifier_WrongConnectionName_IsFail()
        {
            var observation = ExactAssociation();
            observation.ConnectionNameMatches = false;
            var verdict = TargetAccountConnectionVerifier.Evaluate(observation);

            Assert.Equal(DiagnosticStatus.FAIL, verdict.Status);
            Assert.Contains(TargetAccountConnectionReasonCodes.ConnectionNameMismatch, verdict.ReasonCodes);
        }

        [Fact]
        public void Verifier_ConnectionNameUnobservable_IsInconclusive()
        {
            var observation = ExactAssociation();
            observation.ConnectionNameObservable = false;
            var verdict = TargetAccountConnectionVerifier.Evaluate(observation);

            Assert.Equal(DiagnosticStatus.INCONCLUSIVE, verdict.Status);
            Assert.Contains(TargetAccountConnectionReasonCodes.IncompleteMetadata, verdict.ReasonCodes);
        }

        // ---------------- verifier: wrong provider ----------------

        [Fact]
        public void Verifier_AccountProviderMismatch_IsFail()
        {
            var observation = ExactAssociation();
            observation.AccountProviderMatches = false;
            var verdict = TargetAccountConnectionVerifier.Evaluate(observation);

            Assert.Equal(DiagnosticStatus.FAIL, verdict.Status);
            Assert.Contains(TargetAccountConnectionReasonCodes.AccountProviderMismatch, verdict.ReasonCodes);
        }

        [Fact]
        public void Verifier_ConnectionProviderMismatch_IsFail()
        {
            var observation = ExactAssociation();
            observation.ConnectionProviderMatches = false;
            var verdict = TargetAccountConnectionVerifier.Evaluate(observation);

            Assert.Equal(DiagnosticStatus.FAIL, verdict.Status);
            Assert.Contains(TargetAccountConnectionReasonCodes.ConnectionProviderMismatch, verdict.ReasonCodes);
        }

        [Fact]
        public void Verifier_ProviderMismatchOutranksUnobservable_IsFail()
        {
            var observation = ExactAssociation();
            observation.ConnectionProviderMatches = false;
            observation.AccountProviderObservable = false;
            var verdict = TargetAccountConnectionVerifier.Evaluate(observation);

            Assert.Equal(DiagnosticStatus.FAIL, verdict.Status);
            Assert.Contains(TargetAccountConnectionReasonCodes.ConnectionProviderMismatch, verdict.ReasonCodes);
        }

        // ---------------- M2 probe wiring ----------------

        [Fact]
        public void M2_ExactAssociation_Passes()
        {
            var provider = ProviderWithSnapshot(ExactAssociation());
            var results = new M2ConnectionProbe().Evaluate(Context(provider));
            Assert.Equal(DiagnosticStatus.PASS, Find(results, "M2.ACCOUNT_CONNECTION_ASSOCIATION").Status);
        }

        [Fact]
        public void M2_WrongConnection_Fails()
        {
            var observation = ExactAssociation();
            observation.ConnectionNameMatches = false;
            var provider = ProviderWithSnapshot(observation);

            var results = new M2ConnectionProbe().Evaluate(Context(provider));
            var association = Find(results, "M2.ACCOUNT_CONNECTION_ASSOCIATION");
            Assert.Equal(DiagnosticStatus.FAIL, association.Status);
            Assert.Equal(ErrorClass.ConfigurationInvalid, association.ErrorClass);
        }

        [Fact]
        public void M2_WrongProvider_Fails()
        {
            var observation = ExactAssociation();
            observation.AccountProviderMatches = false;
            var provider = ProviderWithSnapshot(observation);

            var results = new M2ConnectionProbe().Evaluate(Context(provider));
            Assert.Equal(DiagnosticStatus.FAIL, Find(results, "M2.ACCOUNT_CONNECTION_ASSOCIATION").Status);
        }

        [Fact]
        public void M2_AmbiguousConnections_IsInconclusive_NotPass()
        {
            var observation = ExactAssociation();
            observation.ConnectionCount = 3;
            observation.MatchingConnectionCount = 2;
            var provider = ProviderWithSnapshot(observation);

            var results = new M2ConnectionProbe().Evaluate(Context(provider));
            var association = Find(results, "M2.ACCOUNT_CONNECTION_ASSOCIATION");
            Assert.Equal(DiagnosticStatus.INCONCLUSIVE, association.Status);
            Assert.Contains(TargetAccountConnectionReasonCodes.AmbiguousConnection, association.ReasonCodes);
        }

        [Fact]
        public void M2_MissingAssociationSurface_IsInconclusive_NotPass()
        {
            var provider = new FakeDataProvider();
            provider.Connections.Add(new ConnectionRecord
            {
                ConnectionName = ExpectedConnection,
                ProviderName = ExpectedProvider,
                PriceState = ConnectionState.Connected,
                OrderChannelState = ConnectionState.Connected,
                Role = ConnectionRole.Broker
            });

            var results = new M2ConnectionProbe().Evaluate(Context(provider));
            Assert.Equal(DiagnosticStatus.INCONCLUSIVE, Find(results, "M2.ACCOUNT_CONNECTION_ASSOCIATION").Status);
        }

        [Fact]
        public void M2_AssociationCheck_IsPresentEvenWithNoConnections()
        {
            // The association check must still be reported (as INCONCLUSIVE) when no connection
            // is observable, so it is never silently omitted.
            var provider = new FakeDataProvider();
            var results = new M2ConnectionProbe().Evaluate(Context(provider));
            Assert.Equal(DiagnosticStatus.INCONCLUSIVE, Find(results, "M2.ACCOUNT_CONNECTION_ASSOCIATION").Status);
        }

        // ---------------- provider attribution ----------------

        [Fact]
        public void M2_ProviderNotAttributed_IsInconclusive()
        {
            // A provider value that could not be attributed to the target account's connection or
            // to a single unambiguous connection is never accepted as the evidence provider.
            var provider = new FakeDataProvider
            {
                Snapshot = new DiagnosticSnapshot
                {
                    Provider = new ProviderEvidence
                    {
                        RawProviderValue = ExpectedProvider,
                        ResolutionMethod = "String",
                        ResolutionBasis = ProviderResolutionBasis.Unavailable
                    }
                }
            };
            provider.Connections.Add(new ConnectionRecord
            {
                ConnectionName = ExpectedConnection,
                ProviderName = ExpectedProvider,
                PriceState = ConnectionState.Connected,
                OrderChannelState = ConnectionState.Connected,
                Role = ConnectionRole.Broker
            });

            var results = new M2ConnectionProbe().Evaluate(Context(provider));
            var identity = Find(results, "M2.PROVIDER_IDENTITY");
            Assert.Equal(DiagnosticStatus.INCONCLUSIVE, identity.Status);
            Assert.Contains("PROVIDER_NOT_ATTRIBUTED", identity.ReasonCodes);
        }

        // ---------------- no raw identity leaks ----------------

        [Fact]
        public void M2_AssociationFinding_NeverContainsRawAccountOrConnectionName()
        {
            var provider = ProviderWithSnapshot(ExactAssociation());
            var results = new M2ConnectionProbe().Evaluate(Context(provider));
            var association = Find(results, "M2.ACCOUNT_CONNECTION_ASSOCIATION");

            var text = association.Observation + "|" + association.Method + "|" + association.Limitation
                + "|" + association.NextAction + "|" + string.Join(",", association.ReasonCodes);
            foreach (var evidence in association.Evidence)
            {
                text += "|" + evidence.ObservedValue;
            }

            Assert.DoesNotContain(ExpectedAccount, text);
            Assert.DoesNotContain(ExpectedConnection, text);
        }

        [Fact]
        public void AccountsJson_ConnectionAssociation_IsWrittenWithNoRawIdentity()
        {
            var dataset = new Core.Evidence.EvidenceDataset();
            dataset.Snapshot = new DiagnosticSnapshot { TargetAccountConnection = ExactAssociation() };

            var package = Core.Evidence.EvidencePackageBuilder.Build(RunResult(), dataset);
            Assert.True(package.VerifyIntegrity(out var failure), failure);

            var accounts = package.Read(Core.Evidence.EvidencePackageCatalog.AccountsJson);
            Assert.Contains("\"connectionAssociation\"", accounts);
            Assert.Contains("TARGET_ACCOUNT_CONNECTION_MATCH", accounts);

            Assert.DoesNotContain(ExpectedAccount, accounts);
            Assert.DoesNotContain(ExpectedConnection, accounts);
        }

        [Fact]
        public void M2_ProviderAgreementFinding_NeverContainsRawConnectionName()
        {
            // The generic provider-agreement check reports each association; it must reduce the
            // raw connection name to a synthetic token so no finding/log/report can disclose it.
            var provider = new FakeDataProvider();
            provider.Connections.Add(new ConnectionRecord
            {
                ConnectionName = ExpectedConnection,
                ProviderName = ExpectedProvider,
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
                        AccountRef = SyntheticRef.For("account", ExpectedAccount),
                        AccountProvider = ExpectedProvider,
                        HasConnection = true,
                        ConnectionName = ExpectedConnection,
                        ConnectionProvider = ExpectedProvider,
                        ProvidersAgree = true
                    }
                }
            };

            var results = new M2ConnectionProbe().Evaluate(Context(provider));
            var agreement = Find(results, "M2.ACCOUNT_PROVIDER_AGREEMENT");

            var text = agreement.Observation + "|" + agreement.Method + "|" + agreement.Limitation
                + "|" + agreement.NextAction + "|" + string.Join(",", agreement.ReasonCodes);
            foreach (var evidence in agreement.Evidence)
            {
                text += "|" + evidence.ObservedValue;
            }

            Assert.DoesNotContain(ExpectedConnection, text);
            Assert.Contains(SyntheticRef.For("connection", ExpectedConnection), text);
        }

        [Fact]
        public void ConnectionsJson_NeverContainsRawConnectionName()
        {
            var dataset = new Core.Evidence.EvidenceDataset();
            dataset.Connections.Add(new ConnectionRecord
            {
                ConnectionName = ExpectedConnection,
                ProviderName = ExpectedProvider,
                PriceState = ConnectionState.Connected,
                OrderChannelState = ConnectionState.Connected,
                Role = ConnectionRole.Broker
            });

            var package = Core.Evidence.EvidencePackageBuilder.Build(RunResult(), dataset);
            Assert.True(package.VerifyIntegrity(out var failure), failure);

            var connections = package.Read(Core.Evidence.EvidencePackageCatalog.ConnectionsJson);
            Assert.DoesNotContain(ExpectedConnection, connections);
            Assert.Contains("\"connectionRef\"", connections);
            Assert.Contains(SyntheticRef.For("connection", ExpectedConnection), connections);
        }

        [Fact]
        public void AccountsJson_Associations_NeverContainRawConnectionName()
        {
            var dataset = new Core.Evidence.EvidenceDataset();
            dataset.Snapshot = new DiagnosticSnapshot
            {
                AccountAssociations = new List<AccountConnectionAssociation>
                {
                    new AccountConnectionAssociation
                    {
                        AccountRef = SyntheticRef.For("account", ExpectedAccount),
                        AccountProvider = ExpectedProvider,
                        HasConnection = true,
                        ConnectionName = ExpectedConnection,
                        ConnectionProvider = ExpectedProvider,
                        ProvidersAgree = true
                    }
                }
            };

            var package = Core.Evidence.EvidencePackageBuilder.Build(RunResult(), dataset);
            Assert.True(package.VerifyIntegrity(out var failure), failure);

            var accounts = package.Read(Core.Evidence.EvidencePackageCatalog.AccountsJson);
            Assert.DoesNotContain(ExpectedConnection, accounts);
            Assert.Contains("\"connectionRef\"", accounts);
            Assert.Contains(SyntheticRef.For("connection", ExpectedConnection), accounts);
        }

        private static Core.Run.DiagnosticRunResult RunResult()
        {
            return new Core.Run.DiagnosticRunResult
            {
                Mode = Core.Run.DiagnosticMode.Full,
                ModeId = "full",
                SessionId = "rp-20260101-000000-abc123",
                CorrelationId = "corr00000000",
                StartedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                FinishedAtUtc = new DateTime(2026, 1, 1, 0, 0, 2, DateTimeKind.Utc),
                FinalState = Core.Run.RunState.Completed,
                Report = new Core.Report.DiagnosticReport
                {
                    SessionId = "rp-20260101-000000-abc123",
                    CorrelationId = "corr00000000",
                    GeneratedAtUtc = new DateTime(2026, 1, 1, 0, 0, 2, DateTimeKind.Utc)
                }
            };
        }
    }
}
