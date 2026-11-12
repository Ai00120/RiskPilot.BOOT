using RiskPilot.Diagnostics.Nt8.Core.Models;
using RiskPilot.Diagnostics.Nt8.Core.Session;

namespace RiskPilot.Diagnostics.Nt8.Core.Engine.Consumers
{
    /// <summary>
    /// M3 event consumer. Runs on the drain thread and accumulates raw order-lifecycle
    /// evidence (snapshots, updates, executions, positions and order-id classifications)
    /// into the session counters. It never mutates host state and never submits, changes or
    /// cancels an order — it only counts what the host already reported.
    /// </summary>
    public sealed class M3OrderLifecycleConsumer : INormalizedEventConsumer
    {
        private readonly SessionContext _session;

        public M3OrderLifecycleConsumer(SessionContext session)
        {
            _session = session;
        }

        public string Name { get { return "M3.OrderLifecycle"; } }

        public void Consume(NormalizedRecord record)
        {
            if (_session == null || record == null)
            {
                return;
            }

            var counters = _session.Counters;

            if (record is OrderRecord order)
            {
                counters.Increment(RuntimeCounterKeys.OrderSnapshot);
                ClassifyOrderId(counters, order.OrderRef, order.State);
                return;
            }

            if (record is ExecutionRecord)
            {
                counters.Increment(RuntimeCounterKeys.ExecutionUpdate);
                return;
            }

            if (record is PositionRecord)
            {
                counters.Increment(RuntimeCounterKeys.PositionUpdate);
            }
        }

        private static void ClassifyOrderId(RuntimeCounters counters, string orderRef, OrderLifecycleState state)
        {
            switch (Classify(orderRef))
            {
                case OrderIdClass.Null:
                    counters.Increment(RuntimeCounterKeys.OrderIdNull);
                    break;
                case OrderIdClass.Empty:
                    counters.Increment(RuntimeCounterKeys.OrderIdEmpty);
                    break;
                case OrderIdClass.Whitespace:
                    counters.Increment(RuntimeCounterKeys.OrderIdWhitespace);
                    break;
                case OrderIdClass.Valid:
                    counters.Increment(RuntimeCounterKeys.OrderIdValid);
                    break;
                default:
                    counters.Increment(RuntimeCounterKeys.OrderIdUnavailable);
                    break;
            }

            if (IsTerminal(state))
            {
                counters.Increment(RuntimeCounterKeys.OrderTerminalRetained);
            }
        }

        /// <summary>
        /// Classifies an order id. A null value is NULL, an empty value is EMPTY, a
        /// whitespace-only value is WHITESPACE and anything else is VALID. The categories are
        /// deliberately distinct so an investigation can tell them apart.
        /// </summary>
        public static OrderIdClass Classify(string orderRef)
        {
            if (orderRef == null)
            {
                return OrderIdClass.Null;
            }

            if (orderRef.Length == 0)
            {
                return OrderIdClass.Empty;
            }

            for (var i = 0; i < orderRef.Length; i++)
            {
                if (!char.IsWhiteSpace(orderRef[i]))
                {
                    return OrderIdClass.Valid;
                }
            }

            return OrderIdClass.Whitespace;
        }

        private static bool IsTerminal(OrderLifecycleState state)
        {
            return state == OrderLifecycleState.Filled
                || state == OrderLifecycleState.Cancelled
                || state == OrderLifecycleState.Rejected;
        }
    }
}
