using System;
using System.Collections.Generic;
using RiskPilot.Diagnostics.Nt8.Core.Models;
using RiskPilot.Diagnostics.Nt8.Core.Models.Observations;
using RiskPilot.Diagnostics.Nt8.Core.Result;
using RiskPilot.Diagnostics.Nt8.Core.Session;
using RiskPilot.Diagnostics.Nt8.Core.Status;

namespace RiskPilot.Diagnostics.Nt8.Core.Engine.Probes
{
    /// <summary>
    /// M3 — Account &amp; order lifecycle. Investigates F-05 (order-to-fill continuity)
    /// independently of RiskPilot.BOT, using only public read-only APIs: <c>Account.Orders</c>
    /// snapshots, order updates, execution updates and position updates. It classifies every
    /// order id (NULL / EMPTY / WHITESPACE / VALID / UNAVAILABLE), records order↔execution
    /// correlation with a correlation id and timestamp, and reports terminal-state retention.
    /// <para>
    /// It never submits, changes or cancels an order, and it never declares a correlation the
    /// evidence does not support: an unmatched order or execution is reported as
    /// INCONCLUSIVE, not PASS.
    /// </para>
    /// </summary>
    public sealed class M3AccountOrderProbe : IDiagnosticProbe
    {
        public string ModuleId { get { return "M3"; } }

        public string Title { get { return "Account & Order Lifecycle (F-05)"; } }

        public IReadOnlyList<DiagnosticResult> Evaluate(ProbeContext context)
        {
            var results = new List<DiagnosticResult>();
            var now = context.Clock.UtcNow;
            var correlation = context.CorrelationId;
            var snapshot = context.Snapshot;
            var observation = snapshot != null ? snapshot.OrderLifecycle : null;

            if (observation == null)
            {
                results.Add(FindingFactory.Unobservable(ModuleId, "M3.ACCOUNT_SURFACE", correlation, now,
                    "The host did not expose an account/order surface for this pass.",
                    "Connect a SIM account and re-run so Account.Orders is observable."));
                return results;
            }

            results.Add(BuildAccountSurfaceCheck(observation, correlation, now));
            results.Add(BuildOrderIdClassificationCheck(observation, correlation, now));
            results.Add(BuildCorrelationCheck(observation, correlation, now));
            results.Add(BuildTerminalRetentionCheck(observation, correlation, now));
            results.Add(BuildF05Check(observation, correlation, now));
            results.Add(BuildBreadthCheck(observation, correlation, now));

            return results;
        }

        private DiagnosticResult BuildAccountSurfaceCheck(OrderLifecycleObservation observation, string correlation, DateTime now)
        {
            if (!observation.OrderSurfaceObservable)
            {
                return FindingFactory.Unobservable(ModuleId, "M3.ACCOUNT_SURFACE", correlation, now,
                    "No account or order collection was readable through the public API.",
                    "Connect a SIM account so Account.All and Account.Orders are populated.");
            }

            return DiagnosticResult
                .Pass(ModuleId, "M3.ACCOUNT_SURFACE", correlation, now)
                .WithFindingId("M3.ACCOUNT_SURFACE")
                .WithReasonCode("ACCOUNT_SURFACE_OBSERVED")
                .WithObservation("accounts=" + observation.AccountCount
                    + " orderSnapshots=" + observation.OrderSnapshotCount
                    + " orderUpdates=" + observation.OrderUpdateCount
                    + " executionUpdates=" + observation.ExecutionUpdateCount
                    + " positionUpdates=" + observation.PositionUpdateCount)
                .WithSource(FindingFactory.SourcePublicApi)
                .WithMethod("Read-only snapshot of Account.All, Account.Orders and the order/execution/position update events.")
                .WithLimitation("Only the surface exposed while the tool ran is counted; a lifecycle that finished before startup is not observed.")
                .WithNextAction("None.")
                .WithEvidence(
                    DiagnosticEvidence.Observable(FindingFactory.SourcePublicApi, "accounts=" + observation.AccountCount, now),
                    DiagnosticEvidence.Observable(FindingFactory.SourcePublicApi, "orderSnapshots=" + observation.OrderSnapshotCount, now),
                    DiagnosticEvidence.Observable(FindingFactory.SourcePublicApi, "executionUpdates=" + observation.ExecutionUpdateCount, now),
                    DiagnosticEvidence.Observable(FindingFactory.SourcePublicApi, "executionsInScope=" + observation.ExecutionsInScope, now));
        }

        private DiagnosticResult BuildOrderIdClassificationCheck(OrderLifecycleObservation observation, string correlation, DateTime now)
        {
            var counts = observation.OrderIdClassCounts ?? new Dictionary<OrderIdClass, int>();
            var valid = Count(counts, OrderIdClass.Valid);
            var nullCount = Count(counts, OrderIdClass.Null);
            var empty = Count(counts, OrderIdClass.Empty);
            var whitespace = Count(counts, OrderIdClass.Whitespace);
            var unavailable = Count(counts, OrderIdClass.Unavailable);
            var malformed = nullCount + empty + whitespace;

            var evidence = new List<DiagnosticEvidence>();
            foreach (OrderIdClass value in Enum.GetValues(typeof(OrderIdClass)))
            {
                var count = Count(counts, value);
                evidence.Add(DiagnosticEvidence.Observable(FindingFactory.SourcePublicApi,
                    "orderId." + value.ToWire() + "=" + count, now));
            }

            if (malformed > 0)
            {
                return DiagnosticResult
                    .Fail(ModuleId, "M3.ORDERID_CLASSIFICATION", ErrorClass.CorrelationIncomplete, correlation, now)
                    .WithFindingId("M3.ORDERID_CLASSIFICATION")
                    .WithReasonCode("ORDERID_MALFORMED_PRESENT", "NULL=" + nullCount, "EMPTY=" + empty, "WHITESPACE=" + whitespace)
                    .WithObservation("valid=" + valid + " null=" + nullCount + " empty=" + empty + " whitespace=" + whitespace + " unavailable=" + unavailable)
                    .WithSource(FindingFactory.SourcePublicApi)
                    .WithMethod("Classified Order.OrderId as NULL / EMPTY / WHITESPACE / VALID / UNAVAILABLE on each observation.")
                    .WithLimitation("A malformed order id blocks reliable order↔execution correlation for that order.")
                    .WithNextAction("Record the raw (hashed) order ids and times of the malformed orders for the F-05 investigation.")
                    .WithEvidence(evidence.ToArray());
            }

            if (valid == 0)
            {
                return DiagnosticResult
                    .Inconclusive(ModuleId, "M3.ORDERID_CLASSIFICATION", correlation, now)
                    .WithFindingId("M3.ORDERID_CLASSIFICATION")
                    .WithReasonCode("NO_ORDER_IDS_OBSERVED")
                    .WithObservation("No order id was observed (valid=0, total=0).")
                    .WithSource(FindingFactory.SourcePublicApi)
                    .WithMethod("Classified Order.OrderId on each observation.")
                    .WithLimitation("Without at least one order there is nothing to classify.")
                    .WithNextAction("Run a session in which at least one order is present.")
                    .WithEvidence(evidence.ToArray());
            }

            return DiagnosticResult
                .Pass(ModuleId, "M3.ORDERID_CLASSIFICATION", correlation, now)
                .WithFindingId("M3.ORDERID_CLASSIFICATION")
                .WithReasonCode("ALL_ORDER_IDS_VALID", "VALID=" + valid)
                .WithObservation("valid=" + valid + " malformed=0 unavailable=" + unavailable)
                .WithSource(FindingFactory.SourcePublicApi)
                .WithMethod("Classified Order.OrderId as NULL / EMPTY / WHITESPACE / VALID / UNAVAILABLE on each observation.")
                .WithLimitation("Classification is per observation; an id that later became available is not retroactively reclassified.")
                .WithNextAction("None.")
                .WithEvidence(evidence.ToArray());
        }

        private DiagnosticResult BuildCorrelationCheck(OrderLifecycleObservation observation, string correlation, DateTime now)
        {
            var total = observation.OrderSnapshotCount + observation.OrderUpdateCount + observation.ExecutionUpdateCount;
            var unmatchedOrders = observation.UnmatchedTerminalOrderCount;
            var unmatchedExecutions = observation.UnmatchedExecutionCount;

            if (total == 0)
            {
                return DiagnosticResult
                    .Inconclusive(ModuleId, "M3.CORRELATION", correlation, now)
                    .WithFindingId("M3.CORRELATION")
                    .WithReasonCode("NO_LIFECYCLE_EVENTS")
                    .WithObservation("No order/execution event was observed to correlate.")
                    .WithSource(FindingFactory.SourcePublicApi)
                    .WithMethod("Correlated OrderId ↔ Execution.OrderId within the observed event window.")
                    .WithLimitation("Correlation requires at least one order and one execution event.")
                    .WithNextAction("Run a session with fills to exercise order↔execution correlation.");
            }

            var complete = unmatchedOrders == 0 && unmatchedExecutions == 0;
            var evidence = new List<DiagnosticEvidence>
            {
                DiagnosticEvidence.Observable(FindingFactory.SourcePublicApi, "unmatchedTerminalOrders=" + unmatchedOrders, now),
                DiagnosticEvidence.Observable(FindingFactory.SourcePublicApi, "unmatchedExecutions=" + unmatchedExecutions, now)
            };
            foreach (var lifecycleEvent in observation.Events)
            {
                evidence.Add(DiagnosticEvidence.Observable(FindingFactory.SourcePublicApi,
                    "corr=" + lifecycleEvent.CorrelationId
                    + " source=" + lifecycleEvent.Source
                    + " orderIdClass=" + lifecycleEvent.OrderIdClass.ToWire()
                    + " state=" + lifecycleEvent.State.ToWire()
                    + " ingestedAt=" + lifecycleEvent.IngestedAtUtc.ToString("O", System.Globalization.CultureInfo.InvariantCulture), now));
            }

            // Correlation is only PASS when it is positively complete; any unmatched event is
            // INCONCLUSIVE, never PASS, and never asserted as a confirmed link.
            return (complete
                    ? DiagnosticResult.Pass(ModuleId, "M3.CORRELATION", correlation, now)
                    : DiagnosticResult.Inconclusive(ModuleId, "M3.CORRELATION", correlation, now))
                .WithFindingId("M3.CORRELATION")
                .WithReasonCode(complete ? "CORRELATION_COMPLETE" : "CORRELATION_INCOMPLETE")
                .WithObservation("unmatchedTerminalOrders=" + unmatchedOrders + " unmatchedExecutions=" + unmatchedExecutions)
                .WithSource(FindingFactory.SourcePublicApi)
                .WithMethod("Matched Execution.OrderId to an observed Order.OrderId and recorded a correlation id and ingest time per event.")
                .WithLimitation("No link is asserted without a matching id; a missing execution for a terminal order is reported as unmatched, not inferred.")
                .WithNextAction(complete
                    ? "None."
                    : "Review the unmatched events; execution reads may be out of scope, or the order id may be malformed.")
                .WithEvidence(evidence.ToArray());
        }

        private DiagnosticResult BuildTerminalRetentionCheck(OrderLifecycleObservation observation, string correlation, DateTime now)
        {
            return DiagnosticResult
                .Inconclusive(ModuleId, "M3.TERMINAL_RETENTION", correlation, now)
                .WithFindingId("M3.TERMINAL_RETENTION")
                .WithReasonCode("TERMINAL_RETAINED=" + observation.TerminalStateRetainedCount)
                .WithObservation("terminalStateRetained=" + observation.TerminalStateRetainedCount + " orderSnapshots=" + observation.OrderSnapshotCount)
                .WithSource(FindingFactory.SourcePublicApi)
                .WithMethod("Counted orders in a terminal state (Filled / Cancelled / Rejected) still present in Account.Orders.")
                .WithLimitation("NinjaTrader may remove a terminal order from Account.Orders after a retention interval; a zero count is not proof that no order reached a terminal state.")
                .WithNextAction("Correlate the retained terminal orders against the recorder for any order that disappeared before the snapshot.")
                .WithEvidence(DiagnosticEvidence.Observable(FindingFactory.SourcePublicApi, "terminalStateRetained=" + observation.TerminalStateRetainedCount, now));
        }

        private DiagnosticResult BuildF05Check(OrderLifecycleObservation observation, string correlation, DateTime now)
        {
            // F-05 is about order→fill continuity. It can only be decided when executions
            // are in scope and at least one terminal order exists.
            if (!observation.ExecutionsInScope)
            {
                return DiagnosticResult
                    .Inconclusive(ModuleId, "M3.F05_ORDER_FILL_CONTINUITY", correlation, now)
                    .WithFindingId("F-05")
                    .WithReasonCode("EXECUTIONS_OUT_OF_SCOPE")
                    .WithObservation("Execution reads are out of scope for this run, so order→fill continuity cannot be decided.")
                    .WithSource(FindingFactory.SourcePublicApi)
                    .WithMethod("Order↔execution correlation is skipped while executions are out of scope.")
                    .WithLimitation("Without execution reads, an order that filled cannot be distinguished from one that never filled.")
                    .WithNextAction("Authorize execution reads (read-only) and re-run to decide F-05.")
                    .WithEvidence(DiagnosticEvidence.Unobservable(FindingFactory.SourcePublicApi, "EXECUTIONS_OUT_OF_SCOPE", now));
            }

            if (observation.OrderSnapshotCount == 0)
            {
                return DiagnosticResult
                    .Inconclusive(ModuleId, "M3.F05_ORDER_FILL_CONTINUITY", correlation, now)
                    .WithFindingId("F-05")
                    .WithReasonCode("NO_ORDERS_OBSERVED")
                    .WithObservation("No order was observed, so continuity cannot be evaluated.")
                    .WithSource(FindingFactory.SourcePublicApi)
                    .WithMethod("Order↔execution correlation over the observed window.")
                    .WithLimitation("F-05 needs at least one order and its execution events.")
                    .WithNextAction("Run a session with at least one order and one fill.");
            }

            var unmatchedOrders = observation.UnmatchedTerminalOrderCount;
            var unmatchedExecutions = observation.UnmatchedExecutionCount;
            var complete = unmatchedOrders == 0 && unmatchedExecutions == 0;

            return (complete
                    ? DiagnosticResult.Pass(ModuleId, "M3.F05_ORDER_FILL_CONTINUITY", correlation, now)
                    : DiagnosticResult.Inconclusive(ModuleId, "M3.F05_ORDER_FILL_CONTINUITY", correlation, now))
                .WithFindingId("F-05")
                .WithReasonCode(complete ? "ORDER_FILL_CONTINUITY_COMPLETE" : "ORDER_FILL_CONTINUITY_INCOMPLETE")
                .WithObservation("unmatchedTerminalOrders=" + unmatchedOrders + " unmatchedExecutions=" + unmatchedExecutions
                    + " orderSnapshots=" + observation.OrderSnapshotCount)
                .WithSource(FindingFactory.SourcePublicApi)
                .WithMethod("Independent order↔execution correlation from public read-only APIs (no RiskPilot.BOT logic).")
                .WithLimitation("F-05 can only be concluded when every terminal order is matched to its execution; otherwise it stays INCONCLUSIVE.")
                .WithNextAction(complete
                    ? "None."
                    : "Capture raw order and execution evidence (hashed ids, times, correlation ids) for the unmatched set.")
                .WithEvidence(
                    DiagnosticEvidence.Observable(FindingFactory.SourcePublicApi, "unmatchedTerminalOrders=" + unmatchedOrders, now),
                    DiagnosticEvidence.Observable(FindingFactory.SourcePublicApi, "unmatchedExecutions=" + unmatchedExecutions, now));
        }

        private DiagnosticResult BuildBreadthCheck(OrderLifecycleObservation observation, string correlation, DateTime now)
        {
            var multiInstrument = observation.DistinctInstrumentCount > 1;
            var multiStrategy = observation.DistinctStrategyCount > 1;

            return DiagnosticResult
                .Inconclusive(ModuleId, "M3.MULTI_SCOPE", correlation, now)
                .WithFindingId("M3.MULTI_SCOPE")
                .WithReasonCode(
                    "INSTRUMENTS=" + observation.DistinctInstrumentCount,
                    "STRATEGIES=" + observation.DistinctStrategyCount,
                    multiInstrument ? "MULTI_INSTRUMENT" : "SINGLE_INSTRUMENT",
                    multiStrategy ? "MULTI_STRATEGY" : "SINGLE_STRATEGY")
                .WithObservation("instruments=" + observation.DistinctInstrumentCount + " strategies=" + observation.DistinctStrategyCount)
                .WithSource(FindingFactory.SourcePublicApi)
                .WithMethod("Counted distinct instruments and strategies across the observed orders.")
                .WithLimitation("Breadth is a raw observation; it makes no claim about attribution between strategies.")
                .WithNextAction(multiInstrument || multiStrategy
                    ? "Confirm the correlation ids isolate each strategy's orders."
                    : "None.")
                .WithEvidence(
                    DiagnosticEvidence.Observable(FindingFactory.SourcePublicApi, "instruments=" + observation.DistinctInstrumentCount, now),
                    DiagnosticEvidence.Observable(FindingFactory.SourcePublicApi, "strategies=" + observation.DistinctStrategyCount, now));
        }

        private static int Count(IDictionary<OrderIdClass, int> counts, OrderIdClass key)
        {
            return counts.TryGetValue(key, out var value) ? value : 0;
        }
    }
}
