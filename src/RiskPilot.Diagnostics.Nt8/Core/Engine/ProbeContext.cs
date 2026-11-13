using System;
using RiskPilot.Diagnostics.Nt8.Core.Clock;
using RiskPilot.Diagnostics.Nt8.Core.Models.Observations;
using RiskPilot.Diagnostics.Nt8.Core.Session;

namespace RiskPilot.Diagnostics.Nt8.Core.Engine
{
    /// <summary>
    /// Everything a probe needs for one evaluation pass: the read-only data provider, the
    /// session, the clock, and the correlation id. Immutable for the duration of a pass.
    /// The M1–M8 observation snapshot is resolved once and cached so every probe reads the
    /// same point-in-time evidence; a provider that cannot supply it yields null, which the
    /// probes treat as INCONCLUSIVE.
    /// </summary>
    public sealed class ProbeContext
    {
        private readonly object _gate = new object();
        private DiagnosticSnapshot _snapshot;
        private bool _snapshotResolved;

        public ProbeContext(
            IDiagnosticDataProvider dataProvider,
            SessionContext session,
            IClock clock,
            string correlationId)
        {
            DataProvider = dataProvider ?? throw new ArgumentNullException(nameof(dataProvider));
            Session = session ?? throw new ArgumentNullException(nameof(session));
            Clock = clock ?? SystemClock.Instance;
            CorrelationId = correlationId ?? Core.Identity.CorrelationId.New();
        }

        public IDiagnosticDataProvider DataProvider { get; private set; }

        public SessionContext Session { get; private set; }

        public IClock Clock { get; private set; }

        public string CorrelationId { get; private set; }

        /// <summary>
        /// The point-in-time observation snapshot for this pass, or null when the provider
        /// could not supply one. Resolved once and cached; a provider failure yields null
        /// rather than propagating an exception into the probe.
        /// </summary>
        public DiagnosticSnapshot Snapshot
        {
            get
            {
                if (_snapshotResolved)
                {
                    return _snapshot;
                }

                lock (_gate)
                {
                    if (!_snapshotResolved)
                    {
                        try
                        {
                            _snapshot = SnapshotAssembler.Assemble(DataProvider.GetSnapshot(), Session);
                        }
                        catch (Exception)
                        {
                            _snapshot = null;
                        }

                        _snapshotResolved = true;
                    }
                }

                return _snapshot;
            }
        }

        /// <summary>Session-scoped runtime counters populated by the M3–M8 event consumers.</summary>
        public RuntimeCounters Counters
        {
            get { return Session.Counters; }
        }
    }
}
