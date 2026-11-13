using System;
using System.Collections.Generic;
using RiskPilot.Diagnostics.Nt8.Core.Models;
using RiskPilot.Diagnostics.Nt8.Core.Models.Observations;

namespace RiskPilot.Diagnostics.Nt8.Core.Engine
{
    /// <summary>
    /// Wraps an <see cref="IDiagnosticDataProvider"/> and applies a set of snapshot
    /// enrichers after the inner provider has produced its snapshot. This lets the runtime
    /// add evidence it owns (for example the tool-side safety posture, which is about the
    /// tool rather than the host) without the host layer needing to know about it. Every
    /// other call is delegated unchanged.
    /// </summary>
    public sealed class EnrichedDataProvider : IDiagnosticDataProvider
    {
        private readonly IDiagnosticDataProvider _inner;
        private readonly List<Action<DiagnosticSnapshot>> _enrichers = new List<Action<DiagnosticSnapshot>>();

        public EnrichedDataProvider(IDiagnosticDataProvider inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        /// <summary>Adds an enricher applied to each snapshot. Enrichers run in registration order.</summary>
        public void AddEnricher(Action<DiagnosticSnapshot> enricher)
        {
            if (enricher != null)
            {
                _enrichers.Add(enricher);
            }
        }

        public EnvironmentRecord GetEnvironment() { return _inner.GetEnvironment(); }

        public IReadOnlyList<ConnectionRecord> GetConnections() { return _inner.GetConnections(); }

        public ProviderRecord GetProvider() { return _inner.GetProvider(); }

        public IReadOnlyList<AccountRecord> GetAccounts() { return _inner.GetAccounts(); }

        public InstrumentRecord GetInstrument(string instrumentId) { return _inner.GetInstrument(instrumentId); }

        public IReadOnlyList<string> GetPresentApis() { return _inner.GetPresentApis(); }

        public IReadOnlyList<string> GetMissingApis() { return _inner.GetMissingApis(); }

        public Core.Contract.ISecurityScanSource GetSecurityScanSource() { return _inner.GetSecurityScanSource(); }

        public DiagnosticSnapshot GetSnapshot()
        {
            var snapshot = _inner.GetSnapshot();

            // Even when the host cannot produce a snapshot, the tool owns some evidence
            // (its own safety posture). Synthesize an empty snapshot so that evidence is
            // always present; the host-owned members stay null and their modules still
            // report INCONCLUSIVE.
            if (snapshot == null)
            {
                snapshot = new DiagnosticSnapshot();
            }

            foreach (var enricher in _enrichers)
            {
                try
                {
                    enricher(snapshot);
                }
                catch (Exception)
                {
                    // An enricher failure must never abort the diagnostic pass.
                }
            }

            return snapshot;
        }
    }
}
