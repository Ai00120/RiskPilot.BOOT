using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace RiskPilot.Diagnostics.Nt8.Core.Session
{
    /// <summary>
    /// Thread-safe, string-keyed counter registry shared between the ingest consumers (which
    /// run on the drain thread) and the diagnostic probes (which read a snapshot on the
    /// diagnostic pass). Increments use <see cref="Interlocked"/>, so a host callback or
    /// drain-thread update never blocks another thread's progress and never takes a
    /// user-level lock. This is how M3–M7 accumulate raw runtime evidence without touching
    /// the callback hot path.
    /// </summary>
    public sealed class RuntimeCounters
    {
        private readonly ConcurrentDictionary<string, long> _values = new ConcurrentDictionary<string, long>();

        /// <summary>Atomically increments the named counter by one.</summary>
        public void Increment(string key)
        {
            Increment(key, 1);
        }

        /// <summary>Atomically increments the named counter. Non-positive deltas are ignored.</summary>
        public void Increment(string key, long delta)
        {
            if (string.IsNullOrEmpty(key) || delta <= 0)
            {
                return;
            }

            _values.AddOrUpdate(key, delta, (_, current) => current + delta);
        }

        /// <summary>Returns the current value of the counter, or zero when absent.</summary>
        public long Get(string key)
        {
            return !string.IsNullOrEmpty(key) && _values.TryGetValue(key, out var value) ? value : 0;
        }

        /// <summary>Returns an immutable snapshot of every counter.</summary>
        public IReadOnlyDictionary<string, long> Snapshot()
        {
            var copy = new Dictionary<string, long>();
            foreach (var pair in _values)
            {
                copy[pair.Key] = pair.Value;
            }

            return copy;
        }

        public void Reset()
        {
            _values.Clear();
        }
    }

    /// <summary>Well-known counter keys used across modules. Keeping them named avoids typos.</summary>
    public static class RuntimeCounterKeys
    {
        public const string OrderSnapshot = "order.snapshot";
        public const string OrderUpdate = "order.update";
        public const string ExecutionUpdate = "execution.update";
        public const string PositionUpdate = "position.update";
        public const string OrderIdNull = "orderid.null";
        public const string OrderIdEmpty = "orderid.empty";
        public const string OrderIdWhitespace = "orderid.whitespace";
        public const string OrderIdValid = "orderid.valid";
        public const string OrderIdUnavailable = "orderid.unavailable";
        public const string OrderTerminalRetained = "order.terminal.retained";
        public const string OrderUnmatchedTerminal = "order.unmatched.terminal";
        public const string ExecutionUnmatched = "execution.unmatched";

        public const string DomHostInsert = "dom.host.insert";
        public const string DomHostUpdate = "dom.host.update";
        public const string DomHostRemove = "dom.host.remove";
        public const string DomHostReset = "dom.host.reset";
        public const string DomHostSnapshotSync = "dom.host.snapshotSync";
        public const string DomHostSnapshotSyncFailure = "dom.host.snapshotSyncFailure";
        public const string DomHostInsertOob = "dom.host.insertOob";
        public const string DomHostUpdateOob = "dom.host.updateOob";
        public const string DomHostRemoveOob = "dom.host.removeOob";
        public const string DomHostInvalidPrice = "dom.host.invalidPrice";
        public const string DomHostInvalidSize = "dom.host.invalidSize";
        public const string DomHostInvalidPosition = "dom.host.invalidPosition";
        public const string DomHostMalformed = "dom.host.malformed";
        public const string DomHostLastSnapshotSyncTrigger = "dom.host.lastSnapshotSyncTrigger";

        public const string RecorderInsert = "dom.recorder.insert";
        public const string RecorderUpdate = "dom.recorder.update";
        public const string RecorderRemove = "dom.recorder.remove";
        public const string RecorderReset = "dom.recorder.reset";

        public const string QuoteObserved = "md.quote.observed";
        public const string LastTradeObserved = "md.last.observed";

        public const string TradeDirectionBuy = "td.buy";
        public const string TradeDirectionSell = "td.sell";
        public const string TradeDirectionUnknown = "td.unknown";
        public const string TradeDirectionInsideSpread = "td.unknown.insideSpread";
        public const string TradeDirectionNoBbo = "td.unknown.noBbo";
        public const string TradeDirectionBboIncomplete = "td.unknown.bboIncomplete";
        public const string TradeDirectionBboInvalid = "td.unknown.bboInvalid";
        public const string TradeDirectionProviderUnknown = "td.unknown.providerReported";
        public const string TradeDirectionProviderSupplied = "td.providerSupplied";
        public const string TradeDeltaResetObserved = "td.reset.observed";

        public const string StrategyCandidate = "strategy.candidate";
        public const string StrategyValidCandidate = "strategy.candidate.valid";
        public const string StrategyInvalidCandidate = "strategy.candidate.invalid";
        public const string StrategyAllCandidatesInvalid = "strategy.allCandidatesInvalid";
        public const string Stage3ZeroQuantity = "stage3.zeroQuantity";
        public const string RiskGateVeto = "stage4.veto";
        public const string CorrelationComplete = "stage.correlation.complete";
        public const string CorrelationIncomplete = "stage.correlation.incomplete";
    }
}
