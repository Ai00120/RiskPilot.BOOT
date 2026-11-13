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
            results.Add(BuildActiveOrdersCheck(observation, correlation, now));
            results.Add(BuildOrderIdClassificationCheck(observation, correlation, now));
            results.Add(BuildCorrelationCheck(observation, correlation, now));
            results.Add(BuildTerminalRetentionCheck(observation, correlation, now));
            results.Add(BuildF05Check(observation, correlation, now));
            results.Add(BuildBreadthCheck(observation, correlation, now));

            return results;
        }

        private DiagnosticResult BuildAccountSurfaceCheck(OrderLifecycleObservation observation, string correlation, DateTime now)
        {
            // No verified target account means no Portfolio collection was read. Report the
            // missing verification explicitly so the reason is not confused with an empty but
            // observed surface. Fail closed: never a PASS.
            if (!observation.TargetAccountVerified)
            {
                return DiagnosticResult
                    .Inconclusive(ModuleId, "M3.ACCOUNT_SURFACE", correlation, now)
                    .WithFindingId("M3.ACCOUNT_SURFACE")
                    .WithReasonCode("TARGET_ACCOUNT_NOT_VERIFIED")
                    .WithObservation("NOT OBSERVED: no verified target account, so no Portfolio collection was read.")
                    .WithSource(FindingFactory.SourcePublicApi)
                    .WithMethod("Portfolio access is limited to the single M2-verified target account; no collection was read.")
                    .WithLimitation("Without exactly one verified target account the order/position surface is not observed.")
                    .WithNextAction("Confirm the running account is exactly the permanent target identity, then re-run.")
                    .WithEvidence(DiagnosticEvidence.Unobservable(FindingFactory.SourcePublicApi, "TARGET_ACCOUNT_NOT_VERIFIED", now));
            }

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

        /// <summary>
        /// Reports the active (still live) order count derived from real host order states.
        /// <para>
        /// Fail-closed rules: with no readable order collection the count is unobservable;
        /// a positive read-failure count or an unclassifiable state makes the total
        /// untrustworthy; and a positive terminal (completed / cancelled / rejected) count is
        /// reported as evidence but never added to the active total. Only a fully observed,
        /// fully classified collection yields a trustworthy active-order count.
        /// </para>
        /// </summary>
        private DiagnosticResult BuildActiveOrdersCheck(OrderLifecycleObservation observation, string correlation, DateTime now)
        {
            var active = observation.ActiveOrderCount;
            var completed = observation.CompletedOrderCount;
            var cancelled = observation.CancelledOrderCount;
            var rejected = observation.RejectedOrderCount;
            var unclassified = observation.UnclassifiedOrderCount;
            var terminal = completed + cancelled + rejected;

            var evidence = new[]
            {
                DiagnosticEvidence.Observable(FindingFactory.SourcePublicApi, "activeOrders=" + active, now),
                DiagnosticEvidence.Observable(FindingFactory.SourcePublicApi, "completedOrders=" + completed, now),
                DiagnosticEvidence.Observable(FindingFactory.SourcePublicApi, "cancelledOrders=" + cancelled, now),
                DiagnosticEvidence.Observable(FindingFactory.SourcePublicApi, "rejectedOrders=" + rejected, now),
                DiagnosticEvidence.Observable(FindingFactory.SourcePublicApi, "unclassifiedOrders=" + unclassified, now),
                DiagnosticEvidence.Observable(FindingFactory.SourcePublicApi, "orderSnapshots=" + observation.OrderSnapshotCount, now),
                DiagnosticEvidence.Observable(FindingFactory.SourcePublicApi, "orderReadFailures=" + observation.OrderReadFailureCount, now)
            };

            // No verified target account: no Portfolio collection was read, so the active-order
            // count cannot be observed. Fail closed with the missing-verification reason rather
            // than reporting an empty surface.
            if (!observation.TargetAccountVerified)
            {
                return DiagnosticResult
                    .Inconclusive(ModuleId, "M3.ACTIVE_ORDERS", correlation, now)
                    .WithFindingId("M3.ACTIVE_ORDERS")
                    .WithReasonCode("TARGET_ACCOUNT_NOT_VERIFIED")
                    .WithObservation("NOT OBSERVED: no verified target account, so no Portfolio collection was read.")
                    .WithSource(FindingFactory.SourcePublicApi)
                    .WithMethod("Active orders are counted only from the single M2-verified target account's Account.Orders.")
                    .WithLimitation("Without exactly one verified target account the active-order count cannot be trusted.")
                    .WithNextAction("Confirm the running account is exactly the permanent target identity, then re-run.")
                    .WithEvidence(DiagnosticEvidence.Unobservable(FindingFactory.SourcePublicApi, "TARGET_ACCOUNT_NOT_VERIFIED", now));
            }

            // No readable order collection: the active-order count cannot be observed at all.
            if (!observation.OrderCollectionObservable)
            {
                return DiagnosticResult
                    .Inconclusive(ModuleId, "M3.ACTIVE_ORDERS", correlation, now)
                    .WithFindingId("M3.ACTIVE_ORDERS")
                    .WithReasonCode("ORDER_COLLECTION_UNOBSERVABLE")
                    .WithObservation("NOT OBSERVED: no account's Account.Orders collection was readable this pass.")
                    .WithSource(FindingFactory.SourcePublicApi)
                    .WithMethod("Counted active orders (Working / Accepted / PartFilled / pending submit) from Account.Orders.")
                    .WithLimitation("Without a readable order collection the active-order count cannot be trusted.")
                    .WithNextAction("Connect a SIM account and re-run so Account.Orders is readable.")
                    .WithEvidence(DiagnosticEvidence.Unobservable(FindingFactory.SourcePublicApi, "ORDER_COLLECTION_UNOBSERVABLE", now));
            }

            // A collection that could not be read, or an order whose state could not be
            // classified, could hide or misreport an active order. Stay fail-closed rather than
            // publishing a count that might be wrong.
            if (observation.OrderReadFailureCount > 0 || unclassified > 0)
            {
                return DiagnosticResult
                    .Inconclusive(ModuleId, "M3.ACTIVE_ORDERS", correlation, now)
                    .WithFindingId("M3.ACTIVE_ORDERS")
                    .WithReasonCode("ACTIVE_ORDER_COUNT_UNRELIABLE",
                        "READ_FAILURES=" + observation.OrderReadFailureCount,
                        "UNCLASSIFIED=" + unclassified)
                    .WithObservation("activeOrders=" + active + " readFailures=" + observation.OrderReadFailureCount + " unclassified=" + unclassified)
                    .WithSource(FindingFactory.SourcePublicApi)
                    .WithMethod("Counted active orders (Working / Accepted / PartFilled / pending submit) from Account.Orders.")
                    .WithLimitation("An unreadable order collection or an unclassifiable order state means an active order could be missing from the count.")
                    .WithNextAction("Re-run once every account's order collection is readable and every order state is classifiable.")
                    .WithEvidence(evidence);
            }

            // Fully observed and fully classified. Report the exact active count, with the
            // terminal breakdown as corroborating evidence (never folded into the active total).
            return DiagnosticResult
                .Pass(ModuleId, "M3.ACTIVE_ORDERS", correlation, now)
                .WithFindingId("M3.ACTIVE_ORDERS")
                .WithReasonCode("ACTIVE_ORDERS_OBSERVED",
                    "ACTIVE=" + active,
                    "COMPLETED=" + completed,
                    "CANCELLED=" + cancelled,
                    "REJECTED=" + rejected,
                    "TERMINAL=" + terminal)
                .WithObservation("activeOrders=" + active + " completed=" + completed + " cancelled=" + cancelled + " rejected=" + rejected)
                .WithSource(FindingFactory.SourcePublicApi)
                .WithMethod("Counted active orders (Working / Accepted / PartFilled / pending submit) from each account's Account.Orders; terminal orders are counted separately and never as active.")
                .WithLimitation("The count reflects only orders present in Account.Orders at the moment of the snapshot; NinjaTrader may retain or remove terminal orders over time.")
                .WithNextAction("None.")
                .WithEvidence(evidence);
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
