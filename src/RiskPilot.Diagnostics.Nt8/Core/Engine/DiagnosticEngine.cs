using System;
using System.Collections.Generic;
using RiskPilot.Diagnostics.Nt8.Core.Clock;
using RiskPilot.Diagnostics.Nt8.Core.Errors;
using RiskPilot.Diagnostics.Nt8.Core.Identity;
using RiskPilot.Diagnostics.Nt8.Core.Logging;
using RiskPilot.Diagnostics.Nt8.Core.Models;
using RiskPilot.Diagnostics.Nt8.Core.Report;
using RiskPilot.Diagnostics.Nt8.Core.Result;
using RiskPilot.Diagnostics.Nt8.Core.Session;
using RiskPilot.Diagnostics.Nt8.Core.Status;

namespace RiskPilot.Diagnostics.Nt8.Core.Engine
{
    /// <summary>
    /// Orchestrates a diagnostic pass: it runs every registered probe, classifies thrown
    /// errors into a FAIL result (never letting an exception escape to the host), and
    /// folds everything into a report whose aggregate status follows fail-closed
    /// precedence. The engine never lets one probe's failure abort the others.
    /// </summary>
    public sealed class DiagnosticEngine
    {
        private readonly List<IDiagnosticProbe> _probes = new List<IDiagnosticProbe>();
        private readonly IDiagnosticDataProvider _dataProvider;
        private readonly DiagnosticLog _log;
        private readonly IClock _clock;

        public DiagnosticEngine(IDiagnosticDataProvider dataProvider, DiagnosticLog log, IClock clock)
        {
            _dataProvider = dataProvider ?? throw new ArgumentNullException(nameof(dataProvider));
            _log = log ?? new DiagnosticLog(256);
            _clock = clock ?? SystemClock.Instance;
        }

        public IReadOnlyList<IDiagnosticProbe> Probes { get { return _probes; } }

        public void Register(IDiagnosticProbe probe)
        {
            if (probe == null)
            {
                throw new ArgumentNullException(nameof(probe));
            }

            _probes.Add(probe);
        }

        /// <summary>Runs every probe for the session and returns a complete report.</summary>
        public DiagnosticReport Run(SessionContext session)
        {
            return Run(session, null);
        }

        /// <summary>
        /// Runs the probes whose <see cref="IDiagnosticProbe.ModuleId"/> is in
        /// <paramref name="moduleIds"/>. A null or empty filter runs every registered probe.
        /// The filter is applied before execution, so a mode that excludes a module never
        /// runs it and never touches its data source.
        /// </summary>
        public DiagnosticReport Run(SessionContext session, IReadOnlyCollection<string> moduleIds)
        {
            return Run(session, moduleIds, null, null, null);
        }

        /// <summary>
        /// Runs the selected probes and reports progress through the optional callbacks:
        /// <paramref name="onModuleStarted"/> is invoked just before a probe runs and
        /// <paramref name="onModuleCompleted"/> just after, with the probe's results. The
        /// callbacks run on the caller's thread (the run worker), never on a host callback
        /// thread, so they may do work but must not touch a UI directly.
        /// <para>
        /// <paramref name="shouldStop"/> is polled between modules. When it returns true the
        /// remaining modules are skipped and the report holds the results produced so far —
        /// this is how a cancel stops diagnostic processing without interrupting a module
        /// that is already running.
        /// </para>
        /// </summary>
        public DiagnosticReport Run(
            SessionContext session,
            IReadOnlyCollection<string> moduleIds,
            Action<string> onModuleStarted,
            Action<string, IReadOnlyList<DiagnosticResult>> onModuleCompleted,
            Func<bool> shouldStop)
        {
            if (session == null)
            {
                throw new ArgumentNullException(nameof(session));
            }

            var correlationId = CorrelationId.New();
            var context = new ProbeContext(_dataProvider, session, _clock, correlationId);
            var report = new DiagnosticReport
            {
                SessionId = session.SessionId,
                CorrelationId = correlationId,
                GeneratedAtUtc = _clock.UtcNow,
                SessionStartedAtUtc = session.StartedAtUtc,
                SourceTimeBasis = session.SourceTimeBasis.ToWire()
            };

            var environment = SafeGetEnvironment();
            report.EnvironmentClass = environment.Environment.ToWire();

            foreach (var probe in _probes)
            {
                if (!IsSelected(probe, moduleIds))
                {
                    continue;
                }

                if (shouldStop != null && shouldStop())
                {
                    break;
                }

                if (onModuleStarted != null)
                {
                    onModuleStarted(probe.ModuleId);
                }

                IReadOnlyList<DiagnosticResult> results;
                try
                {
                    results = probe.Evaluate(context);
                }
                catch (Exception ex)
                {
                    results = new List<DiagnosticResult>
                    {
                        DiagnosticResult
                            .Fail(probe.ModuleId, probe.ModuleId + ".PROBE_EXCEPTION", ErrorClassifier.Classify(ex), correlationId, _clock.UtcNow)
                            .WithReasonCode(ErrorClassifier.Classify(ex).ToWire())
                            .WithEvidence(DiagnosticEvidence.Observable("DiagnosticEngine", ErrorClassifier.Describe(ex), _clock.UtcNow))
                            .WithRemediation("Probe threw; inspect diagnostic log for the redacted stack summary.")
                    };

                    _log.Write(_clock.UtcNow, LogLevel.Error, correlationId, session.SessionId,
                        "Probe " + probe.ModuleId + " threw: " + ErrorClassifier.Describe(ex));
                }

                foreach (var result in results ?? new List<DiagnosticResult>())
                {
                    report.Add(result);
                    report.AddFinding(ToFinding(result, session.SessionId));
                }

                if (onModuleCompleted != null)
                {
                    onModuleCompleted(probe.ModuleId, results ?? new List<DiagnosticResult>());
                }
            }

            report.Counters = SessionCounters.From(session);

            // Loss of events, a gap, or an implausible timestamp must never be hidden:
            // surface it as a FAIL rather than letting the run look clean.
            if (session.HasDataLoss || session.TimestampInvalidCount > 0)
            {
                var codes = new List<string>();
                if (session.DroppedCount > 0) codes.Add("QUEUE_OVERFLOW");
                if (session.SequenceGapCount > 0) codes.Add("SEQUENCE_GAP");
                if (session.TimestampInvalidCount > 0) codes.Add("TIMESTAMP_INVALID");
                codes.Add("DROPPED=" + session.DroppedCount);
                codes.Add("SEQUENCE_GAPS=" + session.SequenceGapCount);
                codes.Add("TIMESTAMP_INVALID=" + session.TimestampInvalidCount);

                var lossResult = DiagnosticResult
                    .Fail("M0", "M0.EVENT_INTEGRITY", ErrorClass.QueueOverflow, correlationId, _clock.UtcNow)
                    .WithReasonCode(codes.ToArray())
                    .WithRemediation("Increase queue capacity or reduce ingest rate; results are not clean while events are lost or timestamps are implausible.");
                report.Add(lossResult);
                report.AddFinding(ToFinding(lossResult, session.SessionId));
            }

            report.AddUnobservable("NINJATRADER_RUNTIME_VERSION: no public API exposes the exact host build.");
            report.AddUnobservable("PROVIDER_BUILD: no public API exposes the Rithmic provider build/version.");
            report.AddUnobservable("TRADE_AGGRESSOR_SIDE: not derivable from public Level I data.");

            _log.Write(_clock.UtcNow, LogLevel.Information, correlationId, session.SessionId,
                "Diagnostic pass complete. aggregate=" + report.AggregateStatus().ToWire());

            return report;
        }

        private static bool IsSelected(IDiagnosticProbe probe, IReadOnlyCollection<string> moduleIds)
        {
            if (moduleIds == null || moduleIds.Count == 0)
            {
                return true;
            }

            foreach (var id in moduleIds)
            {
                if (string.Equals(id, probe.ModuleId, System.StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private EnvironmentRecord SafeGetEnvironment()
        {
            try
            {
                return _dataProvider.GetEnvironment() ?? new EnvironmentRecord();
            }
            catch (Exception ex)
            {
                _log.Write(_clock.UtcNow, LogLevel.Error, string.Empty, string.Empty,
                    "GetEnvironment failed: " + ErrorClassifier.Describe(ex));
                return new EnvironmentRecord();
            }
        }

        private static DiagnosticFinding ToFinding(DiagnosticResult result, string sessionId)
        {
            return new DiagnosticFinding
            {
                CheckId = result.CheckId,
                ModuleId = result.ModuleId,
                FindingId = result.FindingId,
                Status = result.Status,
                Severity = result.Severity,
                ErrorClass = result.ErrorClass,
                Message = result.RemediationHint,
                Observation = result.Observation,
                Source = result.Source,
                Method = result.Method,
                Limitation = result.Limitation,
                NextAction = result.NextAction,
                ReasonCodes = result.ReasonCodes,
                CorrelationId = result.CorrelationId,
                SessionId = sessionId,
                ObservedAtUtc = result.EvaluatedAtUtc
            };
        }
    }
}
