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
    /// Regression tests for the exact account-identity verification required by RiskPilot.BOT
    /// (D-095/D-096): Account=SimRP.BOT, Connection=My Rithmic, provider=Rithmic,
    /// mode=Simulation, currency=USD. They drive the real verifier and the real M2 probe and
    /// assert the fail-closed rules: a partial/similar match is never accepted, a missing or
    /// unreadable field is INCONCLUSIVE (never PASS), and no raw account or connection name is
    /// ever emitted.
    /// </summary>
    public class AccountIdentityTests
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
        /// A fully observed observation of the exact expected identity. Each test perturbs
        /// exactly one field so the failure mode is unambiguous.
        /// </summary>
        private static AccountIdentityObservation ExactIdentity()
        {
            return new AccountIdentityObservation
            {
                AccountSurfaceObservable = true,
                ObservedAccountCount = 1,
                MatchingAccountCount = 1,
                AccountRef = SyntheticRef.For("account", ExpectedAccount),
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

        // ---------------- verifier: exact match ----------------

        [Fact]
        public void Verifier_ExactIdentity_IsPass()
        {
            var verdict = AccountIdentityVerifier.Evaluate(ExactIdentity());
            Assert.Equal(DiagnosticStatus.PASS, verdict.Status);
            Assert.Contains(AccountIdentityReasonCodes.Match, verdict.ReasonCodes);
            Assert.Empty(verdict.MismatchedFields);
            Assert.Empty(verdict.UnobservableFields);
        }

        // ---------------- verifier: wrong account ----------------

        [Fact]
        public void Verifier_NoAccountNameMatch_IsFail_NotPass()
        {
            var observation = ExactIdentity();
            observation.MatchingAccountCount = 0; // the connected account is not the expected one
            var verdict = AccountIdentityVerifier.Evaluate(observation);

            Assert.Equal(DiagnosticStatus.FAIL, verdict.Status);
            Assert.Contains(AccountIdentityReasonCodes.NameMismatch, verdict.ReasonCodes);
        }

        // ---------------- verifier: similar account name ----------------

        [Fact]
        public void ExpectedIdentity_SimilarNames_AreNotAccepted()
        {
            // A partial or look-alike name must never match the exact required name.
            Assert.False(ExpectedAccountIdentity.AccountNameMatches("SimRP.BO"));
            Assert.False(ExpectedAccountIdentity.AccountNameMatches("SimRP.BOTX"));
            Assert.False(ExpectedAccountIdentity.AccountNameMatches("SimRP.BOT2"));
            Assert.False(ExpectedAccountIdentity.AccountNameMatches("simrp.bot"));
            Assert.False(ExpectedAccountIdentity.AccountNameMatches(" SimRP.BOT"));
            Assert.False(ExpectedAccountIdentity.AccountNameMatches("SimRP.BOT "));

            // Only the exact ordinal value matches.
            Assert.True(ExpectedAccountIdentity.AccountNameMatches(ExpectedAccount));
        }

        [Fact]
        public void ExpectedIdentity_SimilarConnectionNames_AreNotAccepted()
        {
            Assert.False(ExpectedAccountIdentity.ConnectionNameMatches("My Rithmic2"));
            Assert.False(ExpectedAccountIdentity.ConnectionNameMatches("My Rithmi"));
            Assert.False(ExpectedAccountIdentity.ConnectionNameMatches("my rithmic"));
            Assert.False(ExpectedAccountIdentity.ConnectionNameMatches("MyRithmic"));
            Assert.True(ExpectedAccountIdentity.ConnectionNameMatches(ExpectedConnection));
        }

        [Fact]
        public void Verifier_SimilarNameIsZeroExactMatch_IsFail()
        {
            // The host reported one account, but its name was similar rather than exact, so the
            // exact-match count is zero and the result is FAIL, never PASS.
            var observation = ExactIdentity();
            observation.MatchingAccountCount = 0;
            Assert.Equal(DiagnosticStatus.FAIL, AccountIdentityVerifier.Evaluate(observation).Status);
        }

        // ---------------- verifier: missing account ----------------

        [Fact]
        public void Verifier_MissingAccount_IsInconclusive()
        {
            var observation = new AccountIdentityObservation
            {
                AccountSurfaceObservable = true,
                ObservedAccountCount = 0,
                MatchingAccountCount = 0
            };

            var verdict = AccountIdentityVerifier.Evaluate(observation);
            Assert.Equal(DiagnosticStatus.INCONCLUSIVE, verdict.Status);
            Assert.Contains(AccountIdentityReasonCodes.NoAccountObserved, verdict.ReasonCodes);
        }

        [Fact]
        public void Verifier_AccountSurfaceUnobservable_IsInconclusive()
        {
            var observation = new AccountIdentityObservation { AccountSurfaceObservable = false };
            var verdict = AccountIdentityVerifier.Evaluate(observation);

            Assert.Equal(DiagnosticStatus.INCONCLUSIVE, verdict.Status);
            Assert.Contains(AccountIdentityReasonCodes.Unobservable, verdict.ReasonCodes);
        }

        [Fact]
        public void Verifier_StubOrMockValue_IsInconclusive_NeverPass()
        {
            var observation = ExactIdentity();
            observation.IsStubOrMockValue = true;
            var verdict = AccountIdentityVerifier.Evaluate(observation);

            Assert.Equal(DiagnosticStatus.INCONCLUSIVE, verdict.Status);
            Assert.Contains(AccountIdentityReasonCodes.StubOrMock, verdict.ReasonCodes);
        }

        [Fact]
        public void Verifier_AmbiguousExactMatches_IsInconclusive()
        {
            var observation = ExactIdentity();
            observation.ObservedAccountCount = 2;
            observation.MatchingAccountCount = 2;
            var verdict = AccountIdentityVerifier.Evaluate(observation);

            Assert.Equal(DiagnosticStatus.INCONCLUSIVE, verdict.Status);
            Assert.Contains(AccountIdentityReasonCodes.Ambiguous, verdict.ReasonCodes);
        }

        // ---------------- verifier: missing connection ----------------

        [Fact]
        public void Verifier_NoConnection_IsInconclusive()
        {
            var observation = ExactIdentity();
            observation.ConnectionObserved = false;
            var verdict = AccountIdentityVerifier.Evaluate(observation);

            Assert.Equal(DiagnosticStatus.INCONCLUSIVE, verdict.Status);
            Assert.Contains(AccountIdentityReasonCodes.ConnectionNotObserved, verdict.ReasonCodes);
            Assert.Contains(AccountIdentityReasonCodes.ConnectionNotObserved, verdict.UnobservableFields);
        }

        [Fact]
        public void Verifier_WrongConnectionName_IsFail()
        {
            var observation = ExactIdentity();
            observation.ConnectionNameMatches = false;
            var verdict = AccountIdentityVerifier.Evaluate(observation);

            Assert.Equal(DiagnosticStatus.FAIL, verdict.Status);
            Assert.Contains(AccountIdentityReasonCodes.ConnectionNameMismatch, verdict.ReasonCodes);
        }

        [Fact]
        public void Verifier_ConnectionNotConnected_IsInconclusive()
        {
            var observation = ExactIdentity();
            observation.ConnectionConnected = false;
            var verdict = AccountIdentityVerifier.Evaluate(observation);

            Assert.Equal(DiagnosticStatus.INCONCLUSIVE, verdict.Status);
            Assert.Contains(AccountIdentityReasonCodes.ConnectionNotConnected, verdict.ReasonCodes);
        }

        // ---------------- verifier: unavailable metadata ----------------

        [Theory]
        [InlineData("AccountProvider")]
        [InlineData("ConnectionName")]
        [InlineData("ConnectionProvider")]
        [InlineData("ConnectionStatus")]
        [InlineData("Mode")]
        [InlineData("Denomination")]
        public void Verifier_EachUnavailableField_KeepsInconclusive(string field)
        {
            var observation = ExactIdentity();
            switch (field)
            {
                case "AccountProvider":
                    observation.AccountProviderObservable = false;
                    break;
                case "ConnectionName":
                    observation.ConnectionNameObservable = false;
                    break;
                case "ConnectionProvider":
                    observation.ConnectionProviderObservable = false;
                    break;
                case "ConnectionStatus":
                    observation.ConnectionStatusObservable = false;
                    break;
                case "Mode":
                    observation.ModeObservable = false;
                    break;
                case "Denomination":
                    observation.DenominationObservable = false;
                    break;
            }

            var verdict = AccountIdentityVerifier.Evaluate(observation);
            Assert.Equal(DiagnosticStatus.INCONCLUSIVE, verdict.Status);
            Assert.Contains(AccountIdentityReasonCodes.IncompleteMetadata, verdict.ReasonCodes);
            Assert.NotEmpty(verdict.UnobservableFields);
        }

        [Fact]
        public void Verifier_MismatchOutranksUnobservable_IsFail()
        {
            // A definite contradiction (wrong provider) must be FAIL even when another field
            // could not be observed: the identity is proven wrong, not merely unproven.
            var observation = ExactIdentity();
            observation.AccountProviderMatches = false;
            observation.ModeObservable = false;
            var verdict = AccountIdentityVerifier.Evaluate(observation);

            Assert.Equal(DiagnosticStatus.FAIL, verdict.Status);
            Assert.Contains(AccountIdentityReasonCodes.AccountProviderMismatch, verdict.ReasonCodes);
        }

        [Fact]
        public void Verifier_WrongModeAndCurrency_AreFail()
        {
            var wrongMode = ExactIdentity();
            wrongMode.ModeIsSimulation = false;
            Assert.Equal(DiagnosticStatus.FAIL, AccountIdentityVerifier.Evaluate(wrongMode).Status);

            var wrongCurrency = ExactIdentity();
            wrongCurrency.DenominationIsUsd = false;
            Assert.Equal(DiagnosticStatus.FAIL, AccountIdentityVerifier.Evaluate(wrongCurrency).Status);
        }

        // ---------------- M2 probe wiring ----------------

        [Fact]
        public void M2_ExactIdentity_Passes()
        {
            var provider = new FakeDataProvider
            {
                Snapshot = new DiagnosticSnapshot { AccountIdentity = ExactIdentity() }
            };
            provider.Connections.Add(new ConnectionRecord
            {
                ProviderName = ExpectedProvider,
                PriceState = ConnectionState.Connected,
                OrderChannelState = ConnectionState.Connected,
                Role = ConnectionRole.Broker
            });

            var results = new M2ConnectionProbe().Evaluate(Context(provider));
            Assert.Equal(DiagnosticStatus.PASS, Find(results, "M2.ACCOUNT_IDENTITY").Status);
        }

        [Fact]
        public void M2_WrongAccount_Fails()
        {
            var observation = ExactIdentity();
            observation.MatchingAccountCount = 0;
            var provider = new FakeDataProvider
            {
                Snapshot = new DiagnosticSnapshot { AccountIdentity = observation }
            };
            provider.Connections.Add(new ConnectionRecord
            {
                ProviderName = ExpectedProvider,
                PriceState = ConnectionState.Connected,
                OrderChannelState = ConnectionState.Connected,
                Role = ConnectionRole.Broker
            });

            var results = new M2ConnectionProbe().Evaluate(Context(provider));
            var identity = Find(results, "M2.ACCOUNT_IDENTITY");
            Assert.Equal(DiagnosticStatus.FAIL, identity.Status);
            Assert.Equal(ErrorClass.ConfigurationInvalid, identity.ErrorClass);
        }

        [Fact]
        public void M2_MissingAccountSurface_IsInconclusive_NotPass()
        {
            var provider = new FakeDataProvider();
            provider.Connections.Add(new ConnectionRecord
            {
                ProviderName = ExpectedProvider,
                PriceState = ConnectionState.Connected,
                OrderChannelState = ConnectionState.Connected,
                Role = ConnectionRole.Broker
            });

            var results = new M2ConnectionProbe().Evaluate(Context(provider));
            Assert.Equal(DiagnosticStatus.INCONCLUSIVE, Find(results, "M2.ACCOUNT_IDENTITY").Status);
        }

        [Fact]
        public void M2_MissingConnection_IsInconclusive()
        {
            var observation = ExactIdentity();
            observation.ConnectionObserved = false;
            var provider = new FakeDataProvider
            {
                Snapshot = new DiagnosticSnapshot { AccountIdentity = observation }
            };
            provider.Connections.Add(new ConnectionRecord
            {
                ProviderName = ExpectedProvider,
                PriceState = ConnectionState.Connected,
                OrderChannelState = ConnectionState.Connected,
                Role = ConnectionRole.Broker
            });

            var results = new M2ConnectionProbe().Evaluate(Context(provider));
            Assert.Equal(DiagnosticStatus.INCONCLUSIVE, Find(results, "M2.ACCOUNT_IDENTITY").Status);
        }

        [Fact]
        public void M2_UnavailableMetadata_IsInconclusive()
        {
            var observation = ExactIdentity();
            observation.DenominationObservable = false;
            var provider = new FakeDataProvider
            {
                Snapshot = new DiagnosticSnapshot { AccountIdentity = observation }
            };
            provider.Connections.Add(new ConnectionRecord
            {
                ProviderName = ExpectedProvider,
                PriceState = ConnectionState.Connected,
                OrderChannelState = ConnectionState.Connected,
                Role = ConnectionRole.Broker
            });

            var results = new M2ConnectionProbe().Evaluate(Context(provider));
            Assert.Equal(DiagnosticStatus.INCONCLUSIVE, Find(results, "M2.ACCOUNT_IDENTITY").Status);
        }

        [Fact]
        public void M2_IdentityCheck_IsPresentEvenWithNoConnections()
        {
            // The identity check must still be reported (as INCONCLUSIVE) when no connection is
            // observable, so the account identity is never silently omitted.
            var provider = new FakeDataProvider();
            var results = new M2ConnectionProbe().Evaluate(Context(provider));
            Assert.Equal(DiagnosticStatus.INCONCLUSIVE, Find(results, "M2.ACCOUNT_IDENTITY").Status);
        }

        // ---------------- no raw identity leaks ----------------

        [Fact]
        public void M2_IdentityFinding_NeverContainsRawAccountOrConnectionName()
        {
            var provider = new FakeDataProvider
            {
                Snapshot = new DiagnosticSnapshot { AccountIdentity = ExactIdentity() }
            };
            provider.Connections.Add(new ConnectionRecord
            {
                ProviderName = ExpectedProvider,
                PriceState = ConnectionState.Connected,
                OrderChannelState = ConnectionState.Connected,
                Role = ConnectionRole.Broker
            });

            var results = new M2ConnectionProbe().Evaluate(Context(provider));
            var identity = Find(results, "M2.ACCOUNT_IDENTITY");

            var text = identity.Observation + "|" + identity.Method + "|" + identity.Limitation
                + "|" + identity.NextAction + "|" + string.Join(",", identity.ReasonCodes);
            foreach (var evidence in identity.Evidence)
            {
                text += "|" + evidence.ObservedValue;
            }

            Assert.DoesNotContain(ExpectedAccount, text);
            Assert.DoesNotContain(ExpectedConnection, text);
        }

        [Fact]
        public void ExpectedIdentity_Constants_MatchOwnerSpecification()
        {
            Assert.Equal("SimRP.BOT", ExpectedAccountIdentity.AccountName);
            Assert.Equal("My Rithmic", ExpectedAccountIdentity.ConnectionName);
            Assert.Equal("Rithmic", ExpectedAccountIdentity.Provider);
            Assert.Equal("USD", ExpectedAccountIdentity.Currency);
            Assert.Equal("SIMULATION", ExpectedAccountIdentity.SimulationMode);
        }
    }
}
