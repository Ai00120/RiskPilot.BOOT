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
using RiskPilot.Diagnostics.Nt8.Host;
using RiskPilot.Diagnostics.Nt8.Tests.Fakes;
using Xunit;

namespace RiskPilot.Diagnostics.Nt8.Tests
{
    /// <summary>
    /// Tests for the host-free provider identity contract (<see cref="ProviderIdentity"/>).
    /// <para>
    /// The real NinjaTrader 8 <c>Provider</c> enum has no dedicated <c>Rithmic</c> member: an
    /// adapter without one is assigned a generic placeholder slot such as <c>Provider14</c>. These
    /// tests prove the value is classified three-valued — a named provider that matches, a named
    /// provider that differs, or a generic slot that is unproven — so a placeholder never becomes a
    /// false PASS or a false FAIL, and the real mapping stays BLOCKED until owner-authorized
    /// runtime evidence resolves it.
    /// </para>
    /// </summary>
    public class ProviderIdentityTests
    {
        private static readonly IReadOnlyDictionary<string, string> RithmicSlotMapping =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { { "Provider14", "Rithmic" } };

        // ---------------- classification: verified named member ----------------

        [Fact]
        public void NamedMember_MatchingRequiredProvider_IsMatches()
        {
            Assert.Equal(ProviderIdentityKind.Named, ProviderIdentity.Classify("Rithmic"));
            Assert.Equal(ProviderIdentityMatch.Matches,
                ProviderIdentity.Compare("Rithmic", "Rithmic", ProviderIdentity.NoOwnerAuthorizedSlots));
        }

        [Fact]
        public void NamedMember_DifferentProvider_IsDiffers()
        {
            Assert.Equal(ProviderIdentityKind.Named, ProviderIdentity.Classify("Simulator"));
            Assert.Equal(ProviderIdentityMatch.Differs,
                ProviderIdentity.Compare("Simulator", "Rithmic", ProviderIdentity.NoOwnerAuthorizedSlots));
        }

        [Fact]
        public void IsExpectedProvider_TrueForNamedMatch_AndAuthorizedSlot()
        {
            Assert.True(ProviderIdentity.IsExpectedProvider("Rithmic"));
            Assert.False(ProviderIdentity.IsExpectedProvider("Simulator"));
            // D-097: the compiled Owner-authorized mapping resolves Provider14 to Rithmic.
            Assert.True(ProviderIdentity.IsExpectedProvider("Provider14"));
            Assert.False(ProviderIdentity.IsExpectedProvider("Provider15"));
        }

        // ---------------- unknown / placeholder identity ----------------

        [Fact]
        public void Provider14_IsPlaceholderSlot_NotNamed()
        {
            Assert.Equal(ProviderIdentityKind.PlaceholderSlot, ProviderIdentity.Classify("Provider14"));
            Assert.True(ProviderIdentity.IsPlaceholderSlot("Provider14"));
        }

        [Fact]
        public void PlaceholderSlot_WithoutMapping_IsUnresolved_NeverAssumedRithmic()
        {
            // The core fail-closed rule: a placeholder must never be mapped to Rithmic by guesswork.
            // With no mapping it stays Unresolved (INCONCLUSIVE). D-097 supplies exactly one
            // explicit, compiled mapping (Provider14), and only that slot resolves.
            Assert.Equal(ProviderIdentityMatch.Unresolved,
                ProviderIdentity.Compare("Provider14", "Rithmic", ProviderIdentity.NoOwnerAuthorizedSlots));
            Assert.False(ProviderIdentity.IsUnresolved("Provider14"));
            Assert.True(ProviderIdentity.IsUnresolved("Provider15"));
            Assert.False(ProviderIdentity.IsExpectedProvider("Provider15"));
        }

        [Theory]
        [InlineData("Provider1")]
        [InlineData("Provider40")]
        [InlineData("Custom0")]
        [InlineData("Custom45")]
        [InlineData("Unknown")]
        [InlineData("33")] // an undefined enum value is rendered as its number
        public void GenericSlots_ArePlaceholderAndUnresolved(string raw)
        {
            Assert.Equal(ProviderIdentityKind.PlaceholderSlot, ProviderIdentity.Classify(raw));
            Assert.Equal(ProviderIdentityMatch.Unresolved,
                ProviderIdentity.Compare(raw, "Rithmic", ProviderIdentity.NoOwnerAuthorizedSlots));
        }

        [Fact]
        public void EmptyOrNull_IsUnobservable_AndUnresolved()
        {
            Assert.Equal(ProviderIdentityKind.Unobservable, ProviderIdentity.Classify(null));
            Assert.Equal(ProviderIdentityKind.Unobservable, ProviderIdentity.Classify("   "));
            Assert.Equal(ProviderIdentityMatch.Unresolved,
                ProviderIdentity.Compare(null, "Rithmic", ProviderIdentity.NoOwnerAuthorizedSlots));
            Assert.Equal(ProviderIdentityMatch.Unresolved,
                ProviderIdentity.Compare("Rithmic", null, ProviderIdentity.NoOwnerAuthorizedSlots));
        }

        // ---------------- enum/string representation differences ----------------

        [Fact]
        public void EnumToString_And_String_AreClassifiedConsistently()
        {
            // The host reads Options.Provider.ToString(); the classification must not depend on the
            // representation, only on the textual value.
            var asEnumString = NinjaTrader.Cbi.Provider.Provider14.ToString();
            Assert.Equal("Provider14", asEnumString);
            Assert.Equal(ProviderIdentityMatch.Unresolved,
                ProviderIdentity.Compare(asEnumString, "Rithmic", ProviderIdentity.NoOwnerAuthorizedSlots));

            var namedEnumString = NinjaTrader.Cbi.Provider.Simulator.ToString();
            Assert.Equal(ProviderIdentityMatch.Differs,
                ProviderIdentity.Compare(namedEnumString, "Rithmic", ProviderIdentity.NoOwnerAuthorizedSlots));
        }

        [Fact]
        public void StubEnum_HasNoRithmicMember()
        {
            // Guards the offline stub against silently reintroducing a Rithmic member that the real
            // host does not have.
            Assert.DoesNotContain("Rithmic", Enum.GetNames(typeof(NinjaTrader.Cbi.Provider)));
        }

        // ---------------- normalization ----------------

        [Theory]
        [InlineData("  Rithmic  ")]
        [InlineData("rithmic")]
        [InlineData("RITHMIC")]
        public void Normalization_TrimsAndIgnoresCase(string raw)
        {
            Assert.Equal(ProviderIdentityMatch.Matches,
                ProviderIdentity.Compare(raw, "Rithmic", ProviderIdentity.NoOwnerAuthorizedSlots));
        }

        [Fact]
        public void Normalization_PlaceholderSlot_IsCaseInsensitiveAndTrimmed()
        {
            Assert.True(ProviderIdentity.IsPlaceholderSlot("  provider14 "));
            Assert.True(ProviderIdentity.IsPlaceholderSlot("CUSTOM3"));
        }

        // ---------------- owner-authorized slot mapping ----------------

        [Fact]
        public void OwnerAuthorizedSlotMapping_ResolvesPlaceholderToVendor()
        {
            // Only an explicit owner-authorized mapping may accept a placeholder slot.
            Assert.Equal(ProviderIdentityMatch.Matches,
                ProviderIdentity.Compare("Provider14", "Rithmic", RithmicSlotMapping));
            Assert.Equal(ProviderIdentityMatch.Differs,
                ProviderIdentity.Compare("Provider14", "CQG", RithmicSlotMapping));
        }

        [Fact]
        public void OwnerAuthorizedSlotMapping_DoesNotAffectOtherSlots()
        {
            Assert.Equal(ProviderIdentityMatch.Unresolved,
                ProviderIdentity.Compare("Provider15", "Rithmic", RithmicSlotMapping));
        }

        [Fact]
        public void OwnerAuthorizedSlotMapping_NeverRemapsNamedProvider()
        {
            // A named provider already carries a real vendor identity: an owner-authorized slot
            // mapping must not remap it, even when it carries an entry for that name.
            var yahooToRithmic =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { { "Yahoo", "Rithmic" } };

            Assert.Null(ProviderIdentity.Resolve("Yahoo", yahooToRithmic));
            Assert.Equal(ProviderIdentityMatch.Differs,
                ProviderIdentity.Compare("Yahoo", "Rithmic", yahooToRithmic));
        }

        [Fact]
        public void OwnerAuthorizedSlotMapping_ResolvesPlaceholderSlot()
        {
            Assert.Equal("Rithmic", ProviderIdentity.Resolve("Provider14", RithmicSlotMapping));
            Assert.Equal(ProviderIdentityMatch.Matches,
                ProviderIdentity.Compare("Provider14", "Rithmic", RithmicSlotMapping));
        }

        [Fact]
        public void NamedProviders_RetainNormalIdentityBehavior()
        {
            Assert.Equal(ProviderIdentityMatch.Matches,
                ProviderIdentity.Compare("Rithmic", "Rithmic", RithmicSlotMapping));
            Assert.Equal(ProviderIdentityMatch.Differs,
                ProviderIdentity.Compare("Simulator", "Rithmic", RithmicSlotMapping));
        }

        [Fact]
        public void Resolve_NullOrEmptyMapping_IsFailClosed()
        {
            Assert.Null(ProviderIdentity.Resolve("Provider14", null));
            Assert.Null(ProviderIdentity.Resolve("Provider14", ProviderIdentity.NoOwnerAuthorizedSlots));
            Assert.Equal("Rithmic", ProviderIdentity.Resolve("Provider14", ProviderIdentity.OwnerAuthorizedSlots));
            Assert.Null(ProviderIdentity.Resolve(null, RithmicSlotMapping));
            Assert.Null(ProviderIdentity.Resolve("   ", RithmicSlotMapping));
        }

        [Fact]
        public void ProductionMapping_IsCompiledD097Mapping_AndImmutable()
        {
            // D-097 authorizes exactly one slot -> vendor mapping, compiled into BOOT itself.
            Assert.Single(ProviderIdentity.OwnerAuthorizedSlots);
            Assert.Equal("Rithmic", ProviderIdentity.OwnerAuthorizedSlots["Provider14"]);
            Assert.Equal(ProviderIdentityMatch.Matches,
                ProviderIdentity.Compare("Provider14", "Rithmic", ProviderIdentity.OwnerAuthorizedSlots));
            Assert.Equal(ProviderIdentityMatch.Unresolved,
                ProviderIdentity.Compare("Provider15", "Rithmic", ProviderIdentity.OwnerAuthorizedSlots));

            // The mapping is an immutable view: no runtime component can add or replace a slot.
            Assert.Throws<NotSupportedException>(() =>
                ((IDictionary<string, string>)ProviderIdentity.OwnerAuthorizedSlots).Add("Provider15", "Rithmic"));
        }

        [Fact]
        public void ProductionMapping_IsExactMatch_NeverCaseOrPrefixInsensitive()
        {
            Assert.Equal(ProviderIdentityMatch.Unresolved,
                ProviderIdentity.Compare("provider14", "Rithmic", ProviderIdentity.OwnerAuthorizedSlots));
            Assert.Equal(ProviderIdentityMatch.Unresolved,
                ProviderIdentity.Compare("Provider140", "Rithmic", ProviderIdentity.OwnerAuthorizedSlots));
            Assert.Null(ProviderIdentity.Resolve("provider14", ProviderIdentity.OwnerAuthorizedSlots));
        }

        [Fact]
        public void AddOn_DefaultSlotMapping_IsTheCompiledD097Mapping()
        {
            var addOn = new DiagnosticAddOn();
            Assert.Equal(ProviderIdentity.OwnerAuthorizedSlots, addOn.OwnerAuthorizedProviderSlots);
            Assert.Equal(ProviderIdentityMatch.Matches,
                ProviderIdentity.Compare("Provider14", "Rithmic", addOn.OwnerAuthorizedProviderSlots));

            // A caller may still supply an empty mapping (the fail-closed path used by the adapter tests).
            addOn.OwnerAuthorizedProviderSlots = ProviderIdentity.NoOwnerAuthorizedSlots;
            Assert.Equal(ProviderIdentityMatch.Unresolved,
                ProviderIdentity.Compare("Provider14", "Rithmic", addOn.OwnerAuthorizedProviderSlots));
        }

        // ---------------- provider agreement across components ----------------

        [Fact]
        public void Agreement_TwoNamedMembers_Resolves()
        {
            bool agree;
            Assert.True(ProviderIdentity.TryAgree("Rithmic", "rithmic", ProviderIdentity.NoOwnerAuthorizedSlots, out agree));
            Assert.True(agree);

            Assert.True(ProviderIdentity.TryAgree("Rithmic", "Simulator", ProviderIdentity.NoOwnerAuthorizedSlots, out agree));
            Assert.False(agree);
        }

        [Fact]
        public void Agreement_PlaceholderSlotOnEitherSide_IsUnresolved()
        {
            bool agree;
            Assert.False(ProviderIdentity.TryAgree("Provider14", "Provider14", ProviderIdentity.NoOwnerAuthorizedSlots, out agree));
            Assert.False(ProviderIdentity.TryAgree("Rithmic", "Provider14", ProviderIdentity.NoOwnerAuthorizedSlots, out agree));
        }

        [Fact]
        public void Agreement_MappedSlots_Resolve()
        {
            bool agree;
            Assert.True(ProviderIdentity.TryAgree("Provider14", "Provider14", RithmicSlotMapping, out agree));
            Assert.True(agree);
        }

        // ---------------- M2 provider identity probe ----------------

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

        private static FakeDataProvider ProviderWithEvidence(ProviderEvidence evidence)
        {
            var provider = new FakeDataProvider
            {
                Snapshot = new DiagnosticSnapshot { Provider = evidence }
            };
            provider.Connections.Add(new ConnectionRecord
            {
                ProviderName = evidence.RawProviderValue,
                PriceState = ConnectionState.Connected,
                OrderChannelState = ConnectionState.Connected,
                Role = ConnectionRole.Broker
            });
            return provider;
        }

        [Fact]
        public void M2_PlaceholderSlotWithoutMapping_IsInconclusive_Blocked()
        {
            var provider = ProviderWithEvidence(new ProviderEvidence
            {
                RawProviderValue = "Provider14",
                ResolutionMethod = "String",
                ResolutionBasis = ProviderResolutionBasis.SingleConnection,
                MatchesExpectedProvider = false,
                ProviderResolved = false
            });

            var result = Find(new M2ConnectionProbe().Evaluate(Context(provider)), "M2.PROVIDER_IDENTITY");
            Assert.Equal(DiagnosticStatus.INCONCLUSIVE, result.Status);
            Assert.Contains("PROVIDER_SLOT_UNRESOLVED", result.ReasonCodes);
        }

        [Fact]
        public void M2_PlaceholderSlotWithMapping_IsPass()
        {
            var provider = ProviderWithEvidence(new ProviderEvidence
            {
                RawProviderValue = "Provider14",
                ResolutionMethod = "String",
                ResolutionBasis = ProviderResolutionBasis.SingleConnection,
                MatchesExpectedProvider = true,
                ProviderResolved = true
            });

            var result = Find(new M2ConnectionProbe().Evaluate(Context(provider)), "M2.PROVIDER_IDENTITY");
            Assert.Equal(DiagnosticStatus.PASS, result.Status);
        }

        [Fact]
        public void M2_ResolvedNamedProviderThatDiffers_Fails()
        {
            var provider = ProviderWithEvidence(new ProviderEvidence
            {
                RawProviderValue = "Simulator",
                ResolutionMethod = "String",
                ResolutionBasis = ProviderResolutionBasis.SingleConnection,
                MatchesExpectedProvider = false,
                ProviderResolved = true
            });

            var result = Find(new M2ConnectionProbe().Evaluate(Context(provider)), "M2.PROVIDER_IDENTITY");
            Assert.Equal(DiagnosticStatus.FAIL, result.Status);
            Assert.Contains("PROVIDER_NOT_EXPECTED", result.ReasonCodes);
        }

        // ---------------- M2 account/connection association probe ----------------

        private static TargetAccountConnectionObservation Association(
            ProviderIdentityMatch accountProvider,
            ProviderIdentityMatch connectionProvider)
        {
            return new TargetAccountConnectionObservation
            {
                AccountSurfaceObservable = true,
                ObservedAccountCount = 1,
                MatchingAccountCount = 1,
                ConnectionCount = 1,
                MatchingConnectionCount = 1,
                AccountRef = SyntheticRef.For("account", "SimRP.BOT"),
                AccountProviderObservable = true,
                AccountProviderMatch = accountProvider,
                ConnectionObserved = true,
                ConnectionEnumerated = true,
                ConnectionNameObservable = true,
                ConnectionNameMatches = true,
                ConnectionProviderObservable = true,
                ConnectionProviderMatch = connectionProvider
            };
        }

        [Fact]
        public void Association_ExactMatch_IsPass()
        {
            var verdict = TargetAccountConnectionVerifier.Evaluate(
                Association(ProviderIdentityMatch.Matches, ProviderIdentityMatch.Matches));
            Assert.Equal(DiagnosticStatus.PASS, verdict.Status);
        }

        [Fact]
        public void Association_UnresolvedPlaceholderProvider_IsInconclusive()
        {
            var verdict = TargetAccountConnectionVerifier.Evaluate(
                Association(ProviderIdentityMatch.Unresolved, ProviderIdentityMatch.Unresolved));
            Assert.Equal(DiagnosticStatus.INCONCLUSIVE, verdict.Status);
            Assert.Contains(TargetAccountConnectionReasonCodes.AccountProviderUnresolved, verdict.ReasonCodes);
            Assert.Contains(TargetAccountConnectionReasonCodes.ConnectionProviderUnresolved, verdict.ReasonCodes);
        }

        [Fact]
        public void Association_ObservedProviderMismatch_IsFail()
        {
            var verdict = TargetAccountConnectionVerifier.Evaluate(
                Association(ProviderIdentityMatch.Differs, ProviderIdentityMatch.Matches));
            Assert.Equal(DiagnosticStatus.FAIL, verdict.Status);
            Assert.Contains(TargetAccountConnectionReasonCodes.AccountProviderMismatch, verdict.ReasonCodes);
        }

        [Fact]
        public void Association_ConnectionNotEnumerated_IsInconclusive()
        {
            // An account-name match alone is not proof of the association: the account's own
            // connection object must be part of the enumerated set.
            var observation = Association(ProviderIdentityMatch.Matches, ProviderIdentityMatch.Matches);
            observation.ConnectionEnumerated = false;
            var verdict = TargetAccountConnectionVerifier.Evaluate(observation);
            Assert.Equal(DiagnosticStatus.INCONCLUSIVE, verdict.Status);
            Assert.Contains(TargetAccountConnectionReasonCodes.ConnectionNotEnumerated, verdict.ReasonCodes);
        }

        // ---------------- account identity verifier: provider agreement ----------------

        private static AccountIdentityObservation Identity(ProviderIdentityMatch accountProvider, ProviderIdentityMatch connectionProvider)
        {
            return new AccountIdentityObservation
            {
                AccountSurfaceObservable = true,
                ObservedAccountCount = 1,
                MatchingAccountCount = 1,
                AccountRef = SyntheticRef.For("account", "SimRP.BOT"),
                AccountProviderObservable = true,
                AccountProviderMatch = accountProvider,
                ConnectionObserved = true,
                ConnectionNameObservable = true,
                ConnectionNameMatches = true,
                ConnectionProviderObservable = true,
                ConnectionProviderMatch = connectionProvider,
                ConnectionStatusObservable = true,
                ConnectionConnected = true,
                ModeObservable = true,
                ModeIsSimulation = true,
                DenominationObservable = true,
                DenominationIsUsd = true
            };
        }

        [Fact]
        public void Identity_ExactMatch_IsPass()
        {
            Assert.Equal(DiagnosticStatus.PASS,
                AccountIdentityVerifier.Evaluate(Identity(ProviderIdentityMatch.Matches, ProviderIdentityMatch.Matches)).Status);
        }

        [Fact]
        public void Identity_UnresolvedPlaceholderProvider_IsInconclusive()
        {
            var verdict = AccountIdentityVerifier.Evaluate(
                Identity(ProviderIdentityMatch.Unresolved, ProviderIdentityMatch.Unresolved));
            Assert.Equal(DiagnosticStatus.INCONCLUSIVE, verdict.Status);
            Assert.Contains(AccountIdentityReasonCodes.AccountProviderUnresolved, verdict.ReasonCodes);
            Assert.Contains(AccountIdentityReasonCodes.ConnectionProviderUnresolved, verdict.ReasonCodes);
        }

        [Fact]
        public void Identity_ObservedProviderMismatch_IsFail()
        {
            var verdict = AccountIdentityVerifier.Evaluate(
                Identity(ProviderIdentityMatch.Matches, ProviderIdentityMatch.Differs));
            Assert.Equal(DiagnosticStatus.FAIL, verdict.Status);
            Assert.Contains(AccountIdentityReasonCodes.ConnectionProviderMismatch, verdict.ReasonCodes);
        }

        // ---------------- missing instrument metadata: fail closed ----------------

        [Fact]
        public void InstrumentIdentity_PlaceholderProviderRoute_IsInconclusive_NeverPass()
        {
            // The exchange route is Globex-only but the sole connected provider is an unresolved
            // placeholder slot: the venue cannot be confirmed, so the identity must not PASS and the
            // canonical id must not be manufactured.
            var observation = new InstrumentIdentityObservation
            {
                InstrumentSurfaceObservable = true,
                RootMetadataObservable = true,
                ObservedInstrumentCount = 1,
                MatchingInstrumentCount = 1,
                InstrumentRef = SyntheticRef.For("instrument", "ES"),
                RootSymbolObservable = true,
                RootSymbolMatches = true,
                InstrumentTypeObservable = true,
                InstrumentTypeMatches = true,
                CurrencyObservable = true,
                CurrencyMatches = true,
                SymbolParsed = true,
                CanonicalInstrumentId = string.Empty,
                CanonicalIdObservable = false,
                CanonicalIdMatches = false,
                ExchangeMetadataObservable = true,
                SelectedRouteIsGlobex = true,
                ConfiguredExchangesAreGlobexOnly = true,
                ProviderRouteIsRithmicOnly = false,
                ProviderRouteResolved = false,
                ExchangeConfirmedCme = false
            };

            var verdict = InstrumentIdentityVerifier.Evaluate(observation);
            Assert.NotEqual(DiagnosticStatus.PASS, verdict.Status);
            Assert.Contains(InstrumentIdentityReasonCodes.ProviderRouteUnresolved, verdict.ReasonCodes);
        }

        [Fact]
        public void InstrumentIdentity_MissingMetadata_IsInconclusive()
        {
            var observation = new InstrumentIdentityObservation
            {
                ExchangeMetadataObservable = false
            };

            var verdict = InstrumentIdentityVerifier.Evaluate(observation);
            Assert.Equal(DiagnosticStatus.INCONCLUSIVE, verdict.Status);
            Assert.NotEqual(DiagnosticStatus.PASS, verdict.Status);
        }
    }
}
