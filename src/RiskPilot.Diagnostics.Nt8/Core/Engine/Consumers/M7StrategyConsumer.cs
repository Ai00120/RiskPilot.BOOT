using RiskPilot.Diagnostics.Nt8.Core.Models;
using RiskPilot.Diagnostics.Nt8.Core.Session;

namespace RiskPilot.Diagnostics.Nt8.Core.Engine.Consumers
{
    /// <summary>
    /// M7 event consumer. Runs on the drain thread and accumulates strategy/risk-gate evidence
    /// from the read-only <see cref="StrategyRecord"/> observations supplied by the diagnostic
    /// contract. Each strategy is recorded into the session's
    /// <see cref="StrategyEvidenceAccumulator"/>, which attributes every reason code to a
    /// pipeline stage using the published <see cref="StrategyReasonCodes"/> catalog.
    /// <para>
    /// It never interprets a count as a cause and never declares an observed figure explained.
    /// A reason code that is not in the catalog is counted as unattributed rather than dropped,
    /// so the module can report that it could not place it. It is strictly read-only: it only
    /// accumulates evidence.
    /// </para>
    /// </summary>
    public sealed class M7StrategyConsumer : INormalizedEventConsumer
    {
        /// <summary>Counter key prefix for a per-reason-code count.</summary>
        public const string ReasonCodePrefix = "strategy.reason.";

        private readonly SessionContext _session;

        public M7StrategyConsumer(SessionContext session)
        {
            _session = session;
        }

        public string Name { get { return "M7.Strategy"; } }

        public void Consume(NormalizedRecord record)
        {
            if (_session == null || !(record is StrategyRecord strategy))
            {
                return;
            }

            // Per-strategy detail (which a flat counter cannot hold) lives in the accumulator.
            _session.StrategyEvidence.Add(strategy);

            // The reason-code counts are also mirrored into the flat session counters, so the
            // same distribution can be derived from the counter stream alone (for example when
            // only counters survive a restart) without losing the detail the accumulator holds.
            var counters = _session.Counters;
            var reasonCodes = strategy.ReasonCodes ?? new string[0];
            foreach (var reason in reasonCodes)
            {
                if (!string.IsNullOrEmpty(reason))
                {
                    counters.Increment(ReasonCodePrefix + reason);
                }
            }
        }
    }
}
