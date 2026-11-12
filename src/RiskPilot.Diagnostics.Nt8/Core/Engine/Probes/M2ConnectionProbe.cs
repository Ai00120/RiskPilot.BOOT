using System.Collections.Generic;
using RiskPilot.Diagnostics.Nt8.Core.Models;
using RiskPilot.Diagnostics.Nt8.Core.Models.Observations;
using RiskPilot.Diagnostics.Nt8.Core.Result;
using RiskPilot.Diagnostics.Nt8.Core.Status;

namespace RiskPilot.Diagnostics.Nt8.Core.Engine.Probes
{
    /// <summary>
    /// M2 — Connection &amp; provider identity. Reports the price/order feed states, resolves
    /// the provider by string comparison (the provider enum has no guaranteed Rithmic member —
    /// this is exactly what BOT PR #275 changed), distinguishes a real runtime value from a
    /// stub/mock value, checks account-to-connection association, and counts reconnects.
    /// Missing connections are INCONCLUSIVE, never PASS.
    /// </summary>
    public sealed class M2ConnectionProbe : IDiagnosticProbe
    {
        public const string ExpectedProvider = "Rithmic";

        public string ModuleId { get { return "M2"; } }

        public string Title { get { return "Connection & Provider Identity"; } }

        public IReadOnlyList<DiagnosticResult> Evaluate(ProbeContext context)
        {
            var results = new List<DiagnosticResult>();
            var now = context.Clock.UtcNow;
            var correlation = context.CorrelationId;
            var connections = context.DataProvider.GetConnections() ?? new List<ConnectionRecord>();

            if (connections.Count == 0)
            {
                results.Add(DiagnosticResult
                    .Inconclusive(ModuleId, "M2.CONNECTIONS", correlation, now)
                    .WithFindingId("M2.CONNECTIONS")
                    .WithReasonCode("NO_CONNECTIONS_OBSERVED")
                    .WithObservation("connections=0")
                    .WithSource(FindingFactory.SourcePublicApi)
                    .WithMethod("Read of Connection.Connections.")
                    .WithLimitation("No connection was observable, so no feed state can be proven.")
                    .WithNextAction("Connect the provider in NinjaTrader and re-run the diagnostic pass.")
                    .WithEvidence(DiagnosticEvidence.Observable(FindingFactory.SourcePublicApi, "connections=0", now))
                    .WithRemediation("No connection was observable; connect the provider and re-run."));
                results.Add(BuildAccountIdentityCheck(context, correlation, now));
                results.Add(DiagnosticResult
                    .NotApplicable(ModuleId, "M2.PROVIDER_IDENTITY", correlation, now)
                    .WithFindingId("M2.PROVIDER_IDENTITY")
                    .WithReasonCode("NO_CONNECTION_FOR_PROVIDER")
                    .WithObservation("No connection is present, so provider identity is not applicable."));
                return results;
            }

            foreach (var connection in connections)
            {
                var checkId = "M2.CONNECTION." + connection.Role.ToWire();
                var bothConnected = connection.PriceState == ConnectionState.Connected
                    && connection.OrderChannelState == ConnectionState.Connected;

                var result = bothConnected
                    ? DiagnosticResult.Pass(ModuleId, checkId, correlation, now)
                    : DiagnosticResult.Inconclusive(ModuleId, checkId, correlation, now);

                result.WithFindingId(checkId)
                    .WithReasonCode(bothConnected ? "FEEDS_CONNECTED" : "FEED_NOT_CONNECTED")
                    .WithObservation("price=" + connection.PriceState.ToWire() + " order=" + connection.OrderChannelState.ToWire())
                    .WithSource(FindingFactory.SourcePublicApi)
                    .WithMethod("Read of Connection.PriceStatus and Connection.Status (order channel).")
                    .WithLimitation("Price feed and order channel are tracked separately; a single connected state never implies the other.")
                    .WithNextAction(bothConnected ? "None." : "Verify both the price feed and the order channel are connected.")
                    .WithEvidence(
                        DiagnosticEvidence.Observable(FindingFactory.SourcePublicApi, "price=" + connection.PriceState.ToWire(), now),
                        DiagnosticEvidence.Observable(FindingFactory.SourcePublicApi, "order=" + connection.OrderChannelState.ToWire(), now),
                        DiagnosticEvidence.Observable(FindingFactory.SourcePublicApi, "reconnects=" + connection.ReconnectCount, now));
                results.Add(result);
            }

            results.Add(BuildReconnectCheck(connections, correlation, now));
            results.Add(BuildProviderIdentityCheck(context, correlation, now));
            results.Add(BuildAccountIdentityCheck(context, correlation, now));
            results.Add(BuildAccountAssociationCheck(context, correlation, now));

            return results;
        }

        private DiagnosticResult BuildReconnectCheck(IReadOnlyList<ConnectionRecord> connections, string correlation, System.DateTime now)
        {
            var totalReconnects = 0;
            foreach (var connection in connections)
            {
                totalReconnects += connection.ReconnectCount;
            }

            var observed = totalReconnects > 0;
            return DiagnosticResult
                .Inconclusive(ModuleId, "M2.RECONNECT", correlation, now)
                .WithFindingId("M2.RECONNECT")
                .WithReasonCode(observed ? "RECONNECT_OBSERVED" : "NO_RECONNECT_OBSERVED")
                .WithObservation("reconnects=" + totalReconnects)
                .WithSource(FindingFactory.SourcePublicApi)
                .WithMethod("Counted ConnectionStatusUpdate transitions away from and back to Connected.")
                .WithLimitation("A reconnect is only observable if the status event fired while the tool was running; a reconnect before startup is not counted.")
                .WithNextAction(observed
                    ? "Correlate the reconnect window against any data gap reported by M5/M6."
                    : "None.")
                .WithEvidence(DiagnosticEvidence.Observable(FindingFactory.SourcePublicApi, "reconnects=" + totalReconnects, now));
        }

        private DiagnosticResult BuildProviderIdentityCheck(ProbeContext context, string correlation, System.DateTime now)
        {
            var snapshot = context.Snapshot;
            var evidence = snapshot != null ? snapshot.Provider : null;
            var provider = context.DataProvider.GetProvider();

            if (provider == null && evidence == null)
            {
                return DiagnosticResult
                    .Inconclusive(ModuleId, "M2.PROVIDER_IDENTITY", correlation, now)
                    .WithFindingId("M2.PROVIDER_IDENTITY")
                    .WithReasonCode("PROVIDER_UNRESOLVED")
                    .WithObservation("No provider value could be read from the connection options.")
                    .WithSource(FindingFactory.SourcePublicApi)
                    .WithMethod("ConnectOptions.Provider.ToString().")
                    .WithLimitation("Provider identity cannot be proven without a connection.")
                    .WithNextAction("Connect the provider and re-run.");
            }

            var rawValue = evidence != null && !string.IsNullOrEmpty(evidence.RawProviderValue)
                ? evidence.RawProviderValue
                : (provider != null ? provider.Name : string.Empty);
            var isStub = evidence != null && evidence.IsStubOrMockValue;
            var matches = !string.IsNullOrEmpty(rawValue)
                && string.Equals(rawValue, ExpectedProvider, System.StringComparison.OrdinalIgnoreCase);

            // A stub/mock value can never establish the real provider identity.
            DiagnosticResult result;
            if (isStub)
            {
                result = DiagnosticResult
                    .Inconclusive(ModuleId, "M2.PROVIDER_IDENTITY", correlation, now)
                    .WithReasonCode("PROVIDER_VALUE_FROM_STUB_OR_MOCK")
                    .WithLimitation("The observed provider value came from a compile-time stub or test double, not the real host; it cannot establish real provider identity.")
                    .WithNextAction("Run the diagnostic inside the real NinjaTrader host with the real connection.");
            }
            else if (matches)
            {
                result = DiagnosticResult
                    .Pass(ModuleId, "M2.PROVIDER_IDENTITY", correlation, now)
                    .WithReasonCode("EXPECTED_PROVIDER_MATCH")
                    .WithLimitation("Provider identity is resolved by string comparison on the provider enum's ToString(), not a guaranteed enum member.");
            }
            else
            {
                result = DiagnosticResult
                    .Inconclusive(ModuleId, "M2.PROVIDER_IDENTITY", correlation, now)
                    .WithReasonCode("PROVIDER_NOT_EXPECTED")
                    .WithLimitation("The observed provider is not the expected evidence provider; it is not treated as a failure of the host.");
            }

            return result
                .WithFindingId("M2.PROVIDER_IDENTITY")
                .WithObservation("provider=" + rawValue + " resolution=" + (evidence != null ? evidence.ResolutionMethod : "String"))
                .WithSource(isStub ? FindingFactory.SourceUnobservable : FindingFactory.SourcePublicApi)
                .WithMethod("ConnectOptions.Provider.ToString() string comparison (no guaranteed Rithmic enum member).")
                .WithNextAction(isStub
                    ? "Run inside the real host."
                    : (matches ? "None." : "Confirm which provider is configured for the evidence connection."))
                .WithEvidence(
                    DiagnosticEvidence.Observable(FindingFactory.SourcePublicApi, "provider=" + rawValue, now),
                    DiagnosticEvidence.Observable(FindingFactory.SourcePublicApi, "resolution=" + (evidence != null ? evidence.ResolutionMethod : "String"), now),
                    DiagnosticEvidence.Unobservable(FindingFactory.SourcePublicApi,
                        evidence != null ? evidence.ProviderBuildUnobservableReason : "NO_PUBLIC_API_FOR_PROVIDER_BUILD", now));
        }

        /// <summary>
        /// Verifies that the observed account is exactly the permanent RiskPilot.BOT identity
        /// (D-095/D-096): account name, connection name, provider, simulation mode and currency.
        /// The comparison is exact and fail-closed; a missing or unreadable field is
        /// INCONCLUSIVE, and a similar or differently-cased name is not a match. The raw account
        /// and connection names never enter the observation, so no identifier can leak into a
        /// finding or an evidence artifact.
        /// </summary>
        private DiagnosticResult BuildAccountIdentityCheck(ProbeContext context, string correlation, System.DateTime now)
        {
            var snapshot = context.Snapshot;
            var observation = snapshot != null ? snapshot.AccountIdentity : null;
            var verdict = Core.Identity.AccountIdentityVerifier.Evaluate(observation);

            var result = verdict.Status == DiagnosticStatus.PASS
                ? DiagnosticResult.Pass(ModuleId, "M2.ACCOUNT_IDENTITY", correlation, now)
                : (verdict.Status == DiagnosticStatus.FAIL
                    ? DiagnosticResult.Fail(ModuleId, "M2.ACCOUNT_IDENTITY", ErrorClass.ConfigurationInvalid, correlation, now)
                    : DiagnosticResult.Inconclusive(ModuleId, "M2.ACCOUNT_IDENTITY", correlation, now));

            var observationText = "accounts=" + verdict.ObservedAccountCount
                + " exactNameMatches=" + verdict.MatchingAccountCount
                + " mismatchedFields=" + Describe(verdict.MismatchedFields)
                + " unobservableFields=" + Describe(verdict.UnobservableFields);

            var evidence = new List<DiagnosticEvidence>();
            foreach (var code in verdict.ReasonCodes)
            {
                evidence.Add(DiagnosticEvidence.Observable(FindingFactory.SourcePublicApi, "reason=" + code, now));
            }

            evidence.Add(DiagnosticEvidence.Observable(FindingFactory.SourcePublicApi,
                "accounts=" + verdict.ObservedAccountCount, now));
            evidence.Add(DiagnosticEvidence.Observable(FindingFactory.SourcePublicApi,
                "exactNameMatches=" + verdict.MatchingAccountCount, now));

            if (verdict.Status == DiagnosticStatus.INCONCLUSIVE
                && verdict.ObservedAccountCount == 0
                && verdict.MatchingAccountCount == 0)
            {
                evidence.Add(DiagnosticEvidence.Unobservable(FindingFactory.SourcePublicApi,
                    "no account identity was observable", now));
            }

            return result
                .WithFindingId("M2.ACCOUNT_IDENTITY")
                .WithReasonCode(ToArray(verdict.ReasonCodes))
                .WithObservation(observationText)
                .WithSource(verdict.Status == DiagnosticStatus.INCONCLUSIVE && verdict.ObservedAccountCount == 0
                    ? FindingFactory.SourceUnobservable
                    : FindingFactory.SourcePublicApi)
                .WithMethod("Exact comparison of Account.Name, Account.Provider, Account.Connection.Options.Name, "
                    + "Account.Connection.Options.Provider, Account.Connection.Options.Mode and Account.Denomination "
                    + "against the permanent RiskPilot.BOT identity; the raw names are never reported.")
                .WithLimitation("Only the fields the host reports are compared; a field that could not be read keeps the "
                    + "identity unproven (INCONCLUSIVE). The tool reports the result, it never rebinds or selects an account.")
                .WithNextAction(verdict.Status == DiagnosticStatus.PASS
                    ? "None."
                    : "Confirm the running account and connection are exactly the permanent RiskPilot.BOT identity, then re-run.")
                .WithEvidence(evidence.ToArray());
        }

        private static string Describe(IList<string> fields)
        {
            return fields == null || fields.Count == 0 ? "none" : string.Join("|", fields);
        }

        private static string[] ToArray(IList<string> values)
        {
            if (values == null || values.Count == 0)
            {
                return new string[0];
            }

            var array = new string[values.Count];
            for (var i = 0; i < values.Count; i++)
            {
                array[i] = values[i];
            }

            return array;
        }

        private DiagnosticResult BuildAccountAssociationCheck(ProbeContext context, string correlation, System.DateTime now)
        {
            var snapshot = context.Snapshot;
            var associations = snapshot != null && snapshot.AccountAssociations != null
                ? snapshot.AccountAssociations
                : new List<AccountConnectionAssociation>();

            if (associations.Count == 0)
            {
                return DiagnosticResult
                    .Inconclusive(ModuleId, "M2.ACCOUNT_CONNECTION_ASSOCIATION", correlation, now)
                    .WithFindingId("M2.ACCOUNT_CONNECTION_ASSOCIATION")
                    .WithReasonCode("NO_ACCOUNT_ASSOCIATION_OBSERVED")
                    .WithObservation("accounts=0")
                    .WithSource(FindingFactory.SourcePublicApi)
                    .WithMethod("Read of Account.All and Account.Connection.")
                    .WithLimitation("No account was observable, so no association can be proven.")
                    .WithNextAction("Connect an account and re-run.");
            }

            var withConnection = 0;
            var agreeing = 0;
            var mismatched = 0;
            var evidence = new List<DiagnosticEvidence>();
            foreach (var association in associations)
            {
                if (association.HasConnection)
                {
                    withConnection++;
                    if (association.ProvidersAgree)
                    {
                        agreeing++;
                    }
                    else
                    {
                        mismatched++;
                    }
                }

                evidence.Add(DiagnosticEvidence.Observable(FindingFactory.SourcePublicApi,
                    "account=" + association.AccountRef
                    + " connection=" + (association.HasConnection ? association.ConnectionName : "none")
                    + " providersAgree=" + association.ProvidersAgree, now));
            }

            var result = mismatched > 0
                ? DiagnosticResult.Fail(ModuleId, "M2.ACCOUNT_CONNECTION_ASSOCIATION", ErrorClass.DataUnavailable, correlation, now)
                : (withConnection == 0
                    ? DiagnosticResult.Inconclusive(ModuleId, "M2.ACCOUNT_CONNECTION_ASSOCIATION", correlation, now)
                    : DiagnosticResult.Pass(ModuleId, "M2.ACCOUNT_CONNECTION_ASSOCIATION", correlation, now));

            return result
                .WithFindingId("M2.ACCOUNT_CONNECTION_ASSOCIATION")
                .WithReasonCode(
                    "ACCOUNTS=" + associations.Count,
                    "WITH_CONNECTION=" + withConnection,
                    "PROVIDERS_AGREE=" + agreeing,
                    "PROVIDER_MISMATCH=" + mismatched)
                .WithObservation("accounts=" + associations.Count + " withConnection=" + withConnection + " providerMismatch=" + mismatched)
                .WithSource(FindingFactory.SourcePublicApi)
                .WithMethod("Compared the account provider with the provider of its associated connection.")
                .WithLimitation("An account with no reported connection cannot be associated and is counted separately, never assumed.")
                .WithNextAction(mismatched > 0
                    ? "Investigate the account/connection provider mismatch; the account provider must match the connection provider."
                    : "None.")
                .WithEvidence(evidence.ToArray());
        }
    }
}
