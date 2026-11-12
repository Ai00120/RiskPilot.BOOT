using RiskPilot.Diagnostics.Nt8.Core.Models;
using RiskPilot.Diagnostics.Nt8.Core.Session;

namespace RiskPilot.Diagnostics.Nt8.Core.Engine.Consumers
{
    /// <summary>
    /// M7 event consumer. Runs on the drain thread and accumulates raw strategy reason-code
    /// evidence from <see cref="StrategyRecord"/> observations supplied by the read-only
    /// diagnostic contract or telemetry. It counts reason codes verbatim; it never interprets
    /// a count as a cause and never declares an observed figure explained.
    /// </summary>
    public sealed class M7StrategyConsumer : INormalizedEventConsumer
    {
        /// <summary>Counter key prefix for a per-reason-code count.</summary>
        public const string ReasonCodePrefix = "strategy.reason.";

        /// <summary>Well-known Stage 1 reason code meaning every candidate was invalid.</summary>
        public const string AllCandidatesInvalid = "ALL_CANDIDATES_INVALID";

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

            var counters = _session.Counters;
            var reasonCodes = strategy.ReasonCodes ?? new string[0];
            foreach (var reason in reasonCodes)
            {
                if (string.IsNullOrEmpty(reason))
                {
                    continue;
                }

                counters.Increment(ReasonCodePrefix + reason);
                if (string.Equals(reason, AllCandidatesInvalid, System.StringComparison.Ordinal))
                {
                    counters.Increment(RuntimeCounterKeys.StrategyAllCandidatesInvalid);
                }
            }
        }
    }
}
