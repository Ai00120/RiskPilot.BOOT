using System.Collections.Generic;
using RiskPilot.Diagnostics.Nt8.Core.Models;
using RiskPilot.Diagnostics.Nt8.Core.Result;
using RiskPilot.Diagnostics.Nt8.Core.Status;

namespace RiskPilot.Diagnostics.Nt8.Core.Engine.Probes
{
    /// <summary>
    /// M2 — Connection &amp; provider identity. Reports the price/order feed states and
    /// resolves the provider by string comparison (the provider enum has no guaranteed
    /// Rithmic member). Missing connections are INCONCLUSIVE, never PASS.
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
                    .WithReasonCode("NO_CONNECTIONS_OBSERVED")
                    .WithEvidence(DiagnosticEvidence.Observable("NinjaTrader API", "connections=0", now))
                    .WithRemediation("No connection was observable; connect the provider and re-run."));
                results.Add(DiagnosticResult
                    .NotApplicable(ModuleId, "M2.PROVIDER_IDENTITY", correlation, now)
                    .WithReasonCode("NO_CONNECTION_FOR_PROVIDER"));
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

                result.WithReasonCode(bothConnected ? "FEEDS_CONNECTED" : "FEED_NOT_CONNECTED")
                    .WithEvidence(
                        DiagnosticEvidence.Observable("NinjaTrader API", "price=" + connection.PriceState.ToWire(), now),
                        DiagnosticEvidence.Observable("NinjaTrader API", "order=" + connection.OrderChannelState.ToWire(), now),
                        DiagnosticEvidence.Observable("NinjaTrader API", "reconnects=" + connection.ReconnectCount, now));
                results.Add(result);
            }

            var provider = context.DataProvider.GetProvider();
            if (provider == null)
            {
                results.Add(DiagnosticResult
                    .Inconclusive(ModuleId, "M2.PROVIDER_IDENTITY", correlation, now)
                    .WithReasonCode("PROVIDER_UNRESOLVED"));
                return results;
            }

            var matches = provider.IsExpectedEvidenceProvider;
            var providerResult = matches
                ? DiagnosticResult.Pass(ModuleId, "M2.PROVIDER_IDENTITY", correlation, now)
                : DiagnosticResult.Inconclusive(ModuleId, "M2.PROVIDER_IDENTITY", correlation, now);

            providerResult
                .WithReasonCode(matches ? "EXPECTED_PROVIDER_MATCH" : "PROVIDER_NOT_EXPECTED")
                .WithEvidence(
                    DiagnosticEvidence.Observable("NinjaTrader API", "provider=" + provider.Name, now),
                    DiagnosticEvidence.Observable("NinjaTrader API", "resolution=" + provider.ResolutionMethod, now),
                    DiagnosticEvidence.Unobservable("NinjaTrader Public API", provider.UnobservableReason, now));
            results.Add(providerResult);

            return results;
        }
    }
}
