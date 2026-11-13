using System;
using System.Collections.Generic;
using RiskPilot.Diagnostics.Nt8.Core.Contract;
using RiskPilot.Diagnostics.Nt8.Core.Models;

namespace RiskPilot.Diagnostics.Nt8.Host
{
    /// <summary>
    /// Read-only capture that pulls strategy/risk-gate observations from an optional
    /// <see cref="IReadOnlyDiagnosticSource"/> and feeds them into the runtime's session. It
    /// mirrors the M5 market-data capture: it is invoked (best-effort) before a pass, it never
    /// throws, and it never fabricates data.
    /// <para>
    /// No public NinjaTrader API exposes strategy internals, so the only source is the
    /// read-only diagnostic contract a host or RiskPilot component may implement. When no
    /// source is supplied, or the source reports nothing, the session accumulator stays empty
    /// and M7 reports INCONCLUSIVE rather than a fabricated state. The capture never mutates
    /// the source or the host.
    /// </para>
    /// </summary>
    public sealed class StrategyDiagnosticCapture
    {
        private readonly Action<StrategyRecord> _ingest;
        private readonly IReadOnlyDiagnosticSource _source;

        public StrategyDiagnosticCapture(Action<StrategyRecord> ingest, IReadOnlyDiagnosticSource source)
        {
            _ingest = ingest;
            _source = source;
        }

        /// <summary>
        /// True when a read-only source is available. A capture with no source reports nothing.
        /// </summary>
        public bool HasSource { get { return _source != null; } }

        /// <summary>
        /// Pulls the current read-only strategy observations and ingests them. Returns the number
        /// of records ingested. It never throws; a failing source is contained so the host is not
        /// disrupted, and M7 stays INCONCLUSIVE (fail-closed).
        /// </summary>
        public int TryCapture()
        {
            if (_source == null || _ingest == null)
            {
                return 0;
            }

            IReadOnlyList<StrategyRecord> strategies;
            try
            {
                strategies = _source.ReadStrategies();
            }
            catch (Exception)
            {
                return 0;
            }

            if (strategies == null)
            {
                return 0;
            }

            var count = 0;
            foreach (var strategy in strategies)
            {
                if (strategy == null)
                {
                    continue;
                }

                try
                {
                    _ingest(strategy);
                    count++;
                }
                catch (Exception)
                {
                    // A single bad record must not abort the capture; the rest are still ingested.
                }
            }

            return count;
        }
    }
}
