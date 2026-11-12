using System.Collections.Generic;

namespace RiskPilot.Diagnostics.Nt8.Core.Models.Observations
{
    /// <summary>
    /// M9 post-trade audit observation. It holds only already-recorded, read-only evidence
    /// about events that happened before the audit ran: order lifecycle, execution lifecycle,
    /// position updates, <c>Account.Orders</c> snapshots, order-id classification,
    /// correlation/timing and missing or duplicated events.
    /// <para>
    /// The audit never creates, submits, changes or cancels anything. A field that the source
    /// could not provide stays at its default and is reported as missing-with-reason; it is
    /// never inferred.
    /// </para>
    /// </summary>
    public sealed class PostTradeAuditObservation
    {
        /// <summary>True when the recorded order/execution surface was readable at all.</summary>
        public bool AuditSurfaceObservable { get; set; }

        /// <summary>Number of accounts whose recorded events were in scope.</summary>
        public int AccountCount { get; set; }

        /// <summary>Number of <c>Account.Orders</c> snapshots examined.</summary>
        public int OrderSnapshotCount { get; set; }

        /// <summary>Number of recorded order-lifecycle update events examined.</summary>
        public int OrderUpdateCount { get; set; }

        /// <summary>Number of recorded execution-lifecycle events examined.</summary>
        public int ExecutionUpdateCount { get; set; }

        /// <summary>Number of recorded position-update events examined.</summary>
        public int PositionUpdateCount { get; set; }

        /// <summary>True when execution events were in scope for the audit.</summary>
        public bool ExecutionsInScope { get; set; }

        /// <summary>Recorded events that could not be matched to a parent order.</summary>
        public int UnmatchedExecutionCount { get; set; }

        /// <summary>Terminal orders with no recorded execution event.</summary>
        public int UnmatchedTerminalOrderCount { get; set; }

        /// <summary>Duplicate recorded events detected in the audit window.</summary>
        public int DuplicateEventCount { get; set; }

        /// <summary>Events whose recorded timestamp moved backwards relative to the prior event.</summary>
        public int OutOfOrderTimestampCount { get; set; }

        /// <summary>Recorded events whose timestamp was absent or implausible.</summary>
        public int InvalidTimestampCount { get; set; }

        /// <summary>Raw order-id classification counts over the recorded orders.</summary>
        public IDictionary<OrderIdClass, int> OrderIdClassCounts { get; set; }

        /// <summary>Recorded lifecycle events in observation order (never reordered).</summary>
        public IList<OrderLifecycleEvent> Events { get; set; }

        public PostTradeAuditObservation()
        {
            OrderIdClassCounts = new Dictionary<OrderIdClass, int>();
            Events = new List<OrderLifecycleEvent>();
        }
    }
}
