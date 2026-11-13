using System.Collections.Generic;

namespace RiskPilot.Diagnostics.Nt8.Core.Models.Observations
{
    /// <summary>
    /// One correlated lifecycle event observed for an order, with its wall-clock time and
    /// the correlation id that ties it to the originating ingest session. Events are kept
    /// in observation order; the module never reorders them and never infers an event it
    /// did not see.
    /// </summary>
    public sealed class OrderLifecycleEvent
    {
        /// <summary>Non-reversible reference token for the order id.</summary>
        public string OrderRef { get; set; }

        /// <summary>Classification of the raw order id at observation time.</summary>
        public OrderIdClass OrderIdClass { get; set; }

        /// <summary>Normalized lifecycle state observed.</summary>
        public OrderLifecycleState State { get; set; }

        public string InstrumentId { get; set; }

        /// <summary>Source of the event ("Account.OrderUpdate", "Account.ExecutionUpdate", ...).</summary>
        public string Source { get; set; }

        /// <summary>Correlation id that links this event to the ingest session.</summary>
        public string CorrelationId { get; set; }

        /// <summary>Source-reported timestamp (UTC), when present.</summary>
        public System.DateTime? SourceTimestampUtc { get; set; }

        /// <summary>Ingest timestamp (UTC) assigned by the tool.</summary>
        public System.DateTime IngestedAtUtc { get; set; }

        public OrderLifecycleEvent()
        {
            OrderRef = string.Empty;
            InstrumentId = string.Empty;
            Source = string.Empty;
            CorrelationId = string.Empty;
        }
    }

    /// <summary>
    /// M3 account / order lifecycle observation. Counts are raw; the module attributes a
    /// correlation only when the evidence is complete, and otherwise reports the gap.
    /// </summary>
    public sealed class OrderLifecycleObservation
    {
        /// <summary>Number of accounts observed (identities are hashed, never raw).</summary>
        public int AccountCount { get; set; }

        /// <summary>
        /// True when M2 established exactly one verified target account for this pass, so the
        /// order/position collections below were read from that account alone. False means no
        /// Portfolio collection was read (the target was missing, ambiguous, duplicated or
        /// unverified), so the module must stay fail-closed rather than reporting a misleading
        /// zero.
        /// </summary>
        public bool TargetAccountVerified { get; set; }

        /// <summary>Number of order snapshots observed from <c>Account.Orders</c>.</summary>
        public int OrderSnapshotCount { get; set; }

        /// <summary>Number of order-update events observed.</summary>
        public int OrderUpdateCount { get; set; }

        /// <summary>Number of execution-update events observed (only when in scope).</summary>
        public int ExecutionUpdateCount { get; set; }

        /// <summary>Number of position-update events observed.</summary>
        public int PositionUpdateCount { get; set; }

        /// <summary>True when execution reads are in scope for this run.</summary>
        public bool ExecutionsInScope { get; set; }

        /// <summary>Number of terminal-state orders still retained in the snapshot.</summary>
        public int TerminalStateRetainedCount { get; set; }

        /// <summary>
        /// Number of orders whose state classifies as active (still live at the host):
        /// working, accepted, partially filled, or a pending submit. A completed, cancelled
        /// or rejected order is never counted here.
        /// </summary>
        public int ActiveOrderCount { get; set; }

        /// <summary>Number of orders in the Filled (completed) state.</summary>
        public int CompletedOrderCount { get; set; }

        /// <summary>Number of orders in the Cancelled state.</summary>
        public int CancelledOrderCount { get; set; }

        /// <summary>Number of orders in the Rejected state.</summary>
        public int RejectedOrderCount { get; set; }

        /// <summary>
        /// Number of observed orders whose state could not be classified into an activity class.
        /// A positive count makes an active-order total unreliable (fail closed).
        /// </summary>
        public int UnclassifiedOrderCount { get; set; }

        /// <summary>
        /// True when at least one account's <c>Account.Orders</c> collection was readable this
        /// pass (even when it was empty). False means the active-order count is unobservable.
        /// </summary>
        public bool OrderCollectionObservable { get; set; }

        /// <summary>
        /// Number of accounts whose order collection could not be read (threw). A positive
        /// count makes the active-order count unreliable, because an order could be hidden by
        /// the unreadable collection.
        /// </summary>
        public int OrderReadFailureCount { get; set; }

        /// <summary>Number of distinct instruments seen across orders.</summary>
        public int DistinctInstrumentCount { get; set; }

        /// <summary>Number of distinct strategies seen across orders (multi-strategy evidence).</summary>
        public int DistinctStrategyCount { get; set; }

        /// <summary>Raw order-id classification counts.</summary>
        public IDictionary<OrderIdClass, int> OrderIdClassCounts { get; set; }

        /// <summary>Orders that reached a terminal state but whose execution could not be matched.</summary>
        public int UnmatchedTerminalOrderCount { get; set; }

        /// <summary>Executions that could not be matched to an order.</summary>
        public int UnmatchedExecutionCount { get; set; }

        /// <summary>Correlated lifecycle events, in observation order.</summary>
        public IList<OrderLifecycleEvent> Events { get; set; }

        /// <summary>
        /// True when the host exposed the order/execution surface at all. False means the
        /// module reports INCONCLUSIVE for correlation rather than PASS.
        /// </summary>
        public bool OrderSurfaceObservable { get; set; }

        public OrderLifecycleObservation()
        {
            OrderIdClassCounts = new Dictionary<OrderIdClass, int>();
            Events = new List<OrderLifecycleEvent>();
        }
    }
}
