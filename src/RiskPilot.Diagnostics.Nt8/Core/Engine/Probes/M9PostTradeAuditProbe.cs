using System;
using System.Collections.Generic;
using RiskPilot.Diagnostics.Nt8.Core.Models;
using RiskPilot.Diagnostics.Nt8.Core.Models.Observations;
using RiskPilot.Diagnostics.Nt8.Core.Result;
using RiskPilot.Diagnostics.Nt8.Core.Status;

namespace RiskPilot.Diagnostics.Nt8.Core.Engine.Probes
{
    /// <summary>
    /// M9 — Post-Trade Audit. Analyses only events that already happened and were recorded:
    /// order lifecycle, execution lifecycle, position updates, <c>Account.Orders</c>
    /// snapshots, order-id classification, correlation/timing and missing or duplicated
    /// events.
    /// <para>
    /// This module is purely observational. It never submits, changes or cancels an order and
    /// never mutates an account, connection or position — there is no code path in this file
    /// that can create or alter a trade. Missing or unmatched events are reported as
    /// INCONCLUSIVE with the reason, never as PASS.
    /// </para>
    /// </summary>
    public sealed class M9PostTradeAuditProbe : IDiagnosticProbe
    {
        public string ModuleId { get { return "M9"; } }

        public string Title { get { return "Post-Trade Audit"; } }

        public IReadOnlyList<DiagnosticResult> Evaluate(ProbeContext context)
        {
            var results = new List<DiagnosticResult>();
            var now = context.Clock.UtcNow;
            var correlation = context.CorrelationId;
            var snapshot = context.Snapshot;
            var observation = snapshot != null ? snapshot.PostTradeAudit : null;

            if (observation == null)
            {
                results.Add(FindingFactory.Unobservable(ModuleId, "M9.AUDIT_SURFACE", correlation, now,
                    "No recorded post-trade events were supplied by the host layer.",
                    "Run the audit after a session that produced recorded order/execution/position events."));
                return results;
            }

            results.Add(BuildAuditSurfaceCheck(observation, correlation, now));
            results.Add(BuildOrderLifecycleCheck(observation, correlation, now));
            results.Add(BuildExecutionLifecycleCheck(observation, correlation, now));
            results.Add(BuildPositionUpdatesCheck(observation, correlation, now));
            results.Add(BuildOrderSnapshotsCheck(observation, correlation, now));
            results.Add(BuildOrderIdClassificationCheck(observation, correlation, now));
            results.Add(BuildCorrelationAndTimingCheck(observation, correlation, now));
            results.Add(BuildMissingOrDuplicateCheck(observation, correlation, now));

            return results;
        }

        private DiagnosticResult BuildAuditSurfaceCheck(PostTradeAuditObservation observation, string correlation, DateTime now)
        {
            if (!observation.AuditSurfaceObservable)
            {
                return FindingFactory.Unobservable(ModuleId, "M9.AUDIT_SURFACE", correlation, now,
                    "No recorded order/execution surface was readable.",
                    "Ensure a session with recorded order/execution events exists before auditing.");
            }

            return DiagnosticResult
                .Pass(ModuleId, "M9.AUDIT_SURFACE", correlation, now)
                .WithFindingId("M9.AUDIT_SURFACE")
                .WithReasonCode("AUDIT_SURFACE_OBSERVED", "READ_ONLY=true")
                .WithObservation("accounts=" + observation.AccountCount
                    + " orderSnapshots=" + observation.OrderSnapshotCount
                    + " orderUpdates=" + observation.OrderUpdateCount
                    + " executionUpdates=" + observation.ExecutionUpdateCount
                    + " positionUpdates=" + observation.PositionUpdateCount)
                .WithSource(FindingFactory.SourcePublicApi)
                .WithMethod("Read-only examination of recorded Account.Orders snapshots and recorded update events.")
                .WithLimitation("Only events recorded while the tool ran are audited; a lifecycle that finished before startup is not visible.")
                .WithNextAction("None.")
                .WithEvidence(
                    DiagnosticEvidence.Observable(FindingFactory.SourcePublicApi, "orderSnapshots=" + observation.OrderSnapshotCount, now),
                    DiagnosticEvidence.Observable(FindingFactory.SourcePublicApi, "executionUpdates=" + observation.ExecutionUpdateCount, now));
        }

        private DiagnosticResult BuildOrderLifecycleCheck(PostTradeAuditObservation observation, string correlation, DateTime now)
        {
            var events = CountEvents(observation, "Account.Orders", "Account.OrderUpdate");
            return DiagnosticResult
                .Inconclusive(ModuleId, "M9.ORDER_LIFECYCLE", correlation, now)
                .WithFindingId("M9.ORDER_LIFECYCLE")
                .WithReasonCode("ORDER_SNAPSHOTS=" + observation.OrderSnapshotCount, "ORDER_UPDATES=" + observation.OrderUpdateCount, "EVENTS=" + events)
                .WithObservation("orderSnapshots=" + observation.OrderSnapshotCount + " orderUpdates=" + observation.OrderUpdateCount)
                .WithSource(FindingFactory.SourcePublicApi)
                .WithMethod("Walked the recorded order events in observation order.")
                .WithLimitation("Raw counts only; the audit does not reorder events or infer a state transition that was not recorded.")
                .WithNextAction("Compare the recorded transitions against the expected order lifecycle for any gap.")
                .WithEvidence(DiagnosticEvidence.Observable(FindingFactory.SourcePublicApi, "orderLifecycleEvents=" + events, now));
        }

        private DiagnosticResult BuildExecutionLifecycleCheck(PostTradeAuditObservation observation, string correlation, DateTime now)
        {
            if (!observation.ExecutionsInScope)
            {
                return DiagnosticResult
                    .Inconclusive(ModuleId, "M9.EXECUTION_LIFECYCLE", correlation, now)
                    .WithFindingId("M9.EXECUTION_LIFECYCLE")
                    .WithReasonCode("EXECUTIONS_OUT_OF_SCOPE")
                    .WithObservation("Execution events were out of scope for this audit.")
                    .WithSource(FindingFactory.SourcePublicApi)
                    .WithMethod("Execution reads are skipped while they are out of scope.")
                    .WithLimitation("Without execution events, a fill cannot be distinguished from a non-fill.")
                    .WithNextAction("Authorize read-only execution events and re-run the audit.");
            }

            return DiagnosticResult
                .Inconclusive(ModuleId, "M9.EXECUTION_LIFECYCLE", correlation, now)
                .WithFindingId("M9.EXECUTION_LIFECYCLE")
                .WithReasonCode("EXECUTION_UPDATES=" + observation.ExecutionUpdateCount, "IN_SCOPE=true")
                .WithObservation("executionUpdates=" + observation.ExecutionUpdateCount)
                .WithSource(FindingFactory.SourcePublicApi)
                .WithMethod("Walked the recorded execution events in observation order.")
                .WithLimitation("Execution ids are non-reversible references; the audit never resolves them back to a raw id.")
                .WithNextAction("Correlate each execution to its order using the recorded correlation id.")
                .WithEvidence(DiagnosticEvidence.Observable(FindingFactory.SourcePublicApi, "executionUpdates=" + observation.ExecutionUpdateCount, now));
        }

        private DiagnosticResult BuildPositionUpdatesCheck(PostTradeAuditObservation observation, string correlation, DateTime now)
        {
            return DiagnosticResult
                .Inconclusive(ModuleId, "M9.POSITION_UPDATES", correlation, now)
                .WithFindingId("M9.POSITION_UPDATES")
                .WithReasonCode("POSITION_UPDATES=" + observation.PositionUpdateCount)
                .WithObservation("positionUpdates=" + observation.PositionUpdateCount)
                .WithSource(FindingFactory.SourcePublicApi)
                .WithMethod("Walked the recorded position-update events in observation order.")
                .WithLimitation("A position update is a recorded snapshot; the audit does not recompute the position.")
                .WithNextAction("Reconcile the recorded position updates against the recorded executions.")
                .WithEvidence(DiagnosticEvidence.Observable(FindingFactory.SourcePublicApi, "positionUpdates=" + observation.PositionUpdateCount, now));
        }

        private DiagnosticResult BuildOrderSnapshotsCheck(PostTradeAuditObservation observation, string correlation, DateTime now)
        {
            return DiagnosticResult
                .Inconclusive(ModuleId, "M9.ORDER_SNAPSHOTS", correlation, now)
                .WithFindingId("M9.ORDER_SNAPSHOTS")
                .WithReasonCode("ORDER_SNAPSHOTS=" + observation.OrderSnapshotCount)
                .WithObservation("orderSnapshots=" + observation.OrderSnapshotCount)
                .WithSource(FindingFactory.SourcePublicApi)
                .WithMethod("Examined the recorded Account.Orders snapshots.")
                .WithLimitation("NinjaTrader may remove a terminal order from Account.Orders after a retention interval, so a snapshot is a point-in-time view.")
                .WithNextAction("Correlate the snapshots against the recorded update events for any order that disappeared.")
                .WithEvidence(DiagnosticEvidence.Observable(FindingFactory.SourcePublicApi, "orderSnapshots=" + observation.OrderSnapshotCount, now));
        }

        private DiagnosticResult BuildOrderIdClassificationCheck(PostTradeAuditObservation observation, string correlation, DateTime now)
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
                evidence.Add(DiagnosticEvidence.Observable(FindingFactory.SourcePublicApi, "orderId." + value.ToWire() + "=" + Count(counts, value), now));
            }

            if (malformed > 0)
            {
                return DiagnosticResult
                    .Fail(ModuleId, "M9.ORDERID_CLASSIFICATION", ErrorClass.CorrelationIncomplete, correlation, now)
                    .WithFindingId("M9.ORDERID_CLASSIFICATION")
                    .WithReasonCode("ORDERID_MALFORMED_PRESENT", "NULL=" + nullCount, "EMPTY=" + empty, "WHITESPACE=" + whitespace)
                    .WithObservation("valid=" + valid + " null=" + nullCount + " empty=" + empty + " whitespace=" + whitespace + " unavailable=" + unavailable)
                    .WithSource(FindingFactory.SourcePublicApi)
                    .WithMethod("Classified every recorded Order.OrderId as NULL / EMPTY / WHITESPACE / VALID / UNAVAILABLE.")
                    .WithLimitation("A malformed order id blocks reliable correlation for that order.")
                    .WithNextAction("Record the (hashed) order ids and times of the malformed orders for the F-05 investigation.")
                    .WithEvidence(evidence.ToArray());
            }

            if (valid == 0)
            {
                return DiagnosticResult
                    .Inconclusive(ModuleId, "M9.ORDERID_CLASSIFICATION", correlation, now)
                    .WithFindingId("M9.ORDERID_CLASSIFICATION")
                    .WithReasonCode("NO_ORDER_IDS_OBSERVED")
                    .WithObservation("No order id was observed in the recorded events.")
                    .WithSource(FindingFactory.SourcePublicApi)
                    .WithMethod("Classified every recorded Order.OrderId.")
                    .WithLimitation("Without at least one recorded order there is nothing to classify.")
                    .WithNextAction("Audit a session that recorded at least one order.")
                    .WithEvidence(evidence.ToArray());
            }

            return DiagnosticResult
                .Pass(ModuleId, "M9.ORDERID_CLASSIFICATION", correlation, now)
                .WithFindingId("M9.ORDERID_CLASSIFICATION")
                .WithReasonCode("ALL_ORDER_IDS_VALID", "VALID=" + valid)
                .WithObservation("valid=" + valid + " malformed=0 unavailable=" + unavailable)
                .WithSource(FindingFactory.SourcePublicApi)
                .WithMethod("Classified every recorded Order.OrderId as NULL / EMPTY / WHITESPACE / VALID / UNAVAILABLE.")
                .WithLimitation("Classification is per recorded observation; an id that later became available is not retroactively reclassified.")
                .WithNextAction("None.")
                .WithEvidence(evidence.ToArray());
        }

        private DiagnosticResult BuildCorrelationAndTimingCheck(PostTradeAuditObservation observation, string correlation, DateTime now)
        {
            var unmatchedOrders = observation.UnmatchedTerminalOrderCount;
            var unmatchedExecutions = observation.UnmatchedExecutionCount;
            var outOfOrder = observation.OutOfOrderTimestampCount;
            var complete = unmatchedOrders == 0 && unmatchedExecutions == 0 && outOfOrder == 0 && observation.ExecutionsInScope;

            DiagnosticResult result;
            if (!observation.ExecutionsInScope)
            {
                result = DiagnosticResult
                    .Inconclusive(ModuleId, "M9.CORRELATION_TIMING", correlation, now)
                    .WithReasonCode("EXECUTIONS_OUT_OF_SCOPE");
            }
            else if (complete)
            {
                result = DiagnosticResult
                    .Pass(ModuleId, "M9.CORRELATION_TIMING", correlation, now)
                    .WithReasonCode("CORRELATION_COMPLETE");
            }
            else
            {
                result = DiagnosticResult
                    .Inconclusive(ModuleId, "M9.CORRELATION_TIMING", correlation, now)
                    .WithReasonCode("CORRELATION_INCOMPLETE");
            }

            return result
                .WithFindingId("M9.CORRELATION_TIMING")
                .WithObservation("unmatchedTerminalOrders=" + unmatchedOrders + " unmatchedExecutions=" + unmatchedExecutions
                    + " outOfOrderTimestamps=" + outOfOrder)
                .WithSource(FindingFactory.SourcePublicApi)
                .WithMethod("Matched recorded execution events to their orders and checked timestamp monotonicity.")
                .WithLimitation("Correlation is PASS only when every terminal order is matched and no timestamp moved backwards; otherwise it stays INCONCLUSIVE.")
                .WithNextAction(complete
                    ? "None."
                    : "Capture the missing correlation ids and out-of-order timestamps for review.")
                .WithEvidence(
                    DiagnosticEvidence.Observable(FindingFactory.SourcePublicApi, "unmatchedTerminalOrders=" + unmatchedOrders, now),
                    DiagnosticEvidence.Observable(FindingFactory.SourcePublicApi, "unmatchedExecutions=" + unmatchedExecutions, now),
                    DiagnosticEvidence.Observable(FindingFactory.SourcePublicApi, "outOfOrderTimestamps=" + outOfOrder, now));
        }

        private DiagnosticResult BuildMissingOrDuplicateCheck(PostTradeAuditObservation observation, string correlation, DateTime now)
        {
            var duplicates = observation.DuplicateEventCount;
            var invalidTimestamps = observation.InvalidTimestampCount;
            var missing = observation.UnmatchedTerminalOrderCount + observation.UnmatchedExecutionCount;

            // A positive count is itself evidence of a problem and fails regardless. Zero counts
            // only mean "clean" once the recorded order/execution surface was actually
            // observable; with nothing recorded there is nothing to have been complete, so the
            // check stays INCONCLUSIVE rather than reporting a clean PASS on absent data.
            if (duplicates == 0 && invalidTimestamps == 0 && missing == 0 && !observation.AuditSurfaceObservable)
            {
                return FindingFactory.Unobservable(ModuleId, "M9.MISSING_OR_DUPLICATE", correlation, now,
                    "No recorded order/execution surface was observed, so missing or duplicate events could not be evaluated.",
                    "Audit a session that recorded at least one order or execution event.");
            }

            var clean = duplicates == 0 && invalidTimestamps == 0 && missing == 0;
            return (clean
                    ? DiagnosticResult.Pass(ModuleId, "M9.MISSING_OR_DUPLICATE", correlation, now)
                    : DiagnosticResult.Fail(ModuleId, "M9.MISSING_OR_DUPLICATE", ErrorClass.DataUnavailable, correlation, now))
                .WithFindingId("M9.MISSING_OR_DUPLICATE")
                .WithReasonCode("DUPLICATES=" + duplicates, "INVALID_TIMESTAMPS=" + invalidTimestamps, "MISSING=" + missing)
                .WithObservation("duplicates=" + duplicates + " invalidTimestamps=" + invalidTimestamps + " missing=" + missing)
                .WithSource(FindingFactory.SourcePublicApi)
                .WithMethod("Counted duplicate recorded events, invalid timestamps and events with no matching counterpart.")
                .WithLimitation("A non-zero count means the recorded evidence is incomplete for the audit window; the audit must not be read as clean.")
                .WithNextAction(clean
                    ? "None."
                    : "Treat the audit window as incomplete; investigate the missing/duplicated/invalid events.")
                .WithEvidence(
                    DiagnosticEvidence.Observable(FindingFactory.SourcePublicApi, "duplicates=" + duplicates, now),
                    DiagnosticEvidence.Observable(FindingFactory.SourcePublicApi, "invalidTimestamps=" + invalidTimestamps, now),
                    DiagnosticEvidence.Observable(FindingFactory.SourcePublicApi, "missing=" + missing, now));
        }

        private static int CountEvents(PostTradeAuditObservation observation, params string[] sources)
        {
            var total = 0;
            foreach (var lifecycleEvent in observation.Events)
            {
                if (lifecycleEvent == null)
                {
                    continue;
                }

                foreach (var source in sources)
                {
                    if (string.Equals(lifecycleEvent.Source, source, StringComparison.Ordinal))
                    {
                        total++;
                        break;
                    }
                }
            }

            return total;
        }

        private static int Count(IDictionary<OrderIdClass, int> counts, OrderIdClass key)
        {
            return counts.TryGetValue(key, out var value) ? value : 0;
        }
    }
}
