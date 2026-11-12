using System;
using System.Collections.Generic;
using System.Threading;
using RiskPilot.Diagnostics.Nt8.Core.Clock;
using RiskPilot.Diagnostics.Nt8.Core.Engine;
using RiskPilot.Diagnostics.Nt8.Core.Errors;
using RiskPilot.Diagnostics.Nt8.Core.Identity;
using RiskPilot.Diagnostics.Nt8.Core.Logging;
using RiskPilot.Diagnostics.Nt8.Core.Models;
using RiskPilot.Diagnostics.Nt8.Core.Queue;
using RiskPilot.Diagnostics.Nt8.Core.Report;
using RiskPilot.Diagnostics.Nt8.Core.Session;

namespace RiskPilot.Diagnostics.Nt8.Host
{
    /// <summary>
    /// Host-independent diagnostic runtime with an explicit lifecycle:
    /// Initialize -> Start -> Stop -> Dispose. Each transition is exactly-once and
    /// idempotent. Dispose is safe to call at any time, including after a partial
    /// Initialize, and always releases the ingest queue and the drain worker so no data
    /// is silently abandoned.
    /// </summary>
    public sealed class DiagnosticRuntime : IDisposable
    {
        private readonly IClock _clock;
        private readonly IDiagnosticDataProvider _dataProvider;
        private readonly int _queueCapacity;
        private readonly int _duplicateWindow;

        private readonly object _gate = new object();
        private volatile SessionContext _session;
        private DiagnosticLog _log;
        private volatile EventIngestPipeline _pipeline;
        private DiagnosticEngine _engine;
        private volatile NormalizedEventDispatcher _dispatcher;
        private Thread _worker;
        private volatile bool _running;
        private bool _disposed;

        public DiagnosticRuntime(IDiagnosticDataProvider dataProvider, IClock clock, int queueCapacity, int duplicateWindow)
        {
            _dataProvider = dataProvider ?? throw new ArgumentNullException(nameof(dataProvider));
            _clock = clock ?? SystemClock.Instance;
            _queueCapacity = queueCapacity > 0 ? queueCapacity : 4096;
            _duplicateWindow = duplicateWindow > 0 ? duplicateWindow : 8192;
        }

        public bool IsRunning { get { return _running; } }

        public bool IsDisposed { get { lock (_gate) { return _disposed; } } }

        public SessionContext Session { get { lock (_gate) { return _session; } } }

        public DiagnosticLog Log { get { lock (_gate) { return _log; } } }

        /// <summary>True once Initialize has created the session and pipeline.</summary>
        public bool IsInitialized { get { lock (_gate) { return _session != null; } } }

        /// <summary>
        /// Registers a normalized-event consumer. This is the extension point for future
        /// diagnostic modules (M3–M8). Registration is expected before Start; it is also
        /// safe after Start because the dispatcher is copy-on-write. A consumer runs on the
        /// drain thread and never on a host callback thread.
        /// </summary>
        public void RegisterEventConsumer(INormalizedEventConsumer consumer)
        {
            var dispatcher = _dispatcher;
            if (dispatcher == null)
            {
                throw new InvalidOperationException("Initialize must be called before registering a consumer.");
            }

            dispatcher.Register(consumer);
        }

        /// <summary>Count of records delivered to at least one registered consumer.</summary>
        public long ConsumedEventCount
        {
            get
            {
                var dispatcher = _dispatcher;
                return dispatcher == null ? 0 : dispatcher.ConsumedCount;
            }
        }

        /// <summary>Count of consumer invocations that threw and were contained.</summary>
        public long ConsumerErrorCount
        {
            get
            {
                var dispatcher = _dispatcher;
                return dispatcher == null ? 0 : dispatcher.ConsumerErrorCount;
            }
        }

        /// <summary>
        /// Creates the session, log, ingest pipeline and engine, and registers the
        /// probes. Safe to call more than once; only the first call has an effect.
        /// </summary>
        public void Initialize()
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    throw new ObjectDisposedException(nameof(DiagnosticRuntime));
                }

                if (_session != null)
                {
                    return;
                }

                var now = _clock.UtcNow;
                _session = new SessionContext(SessionId.New(now), now);
                _log = new DiagnosticLog(_queueCapacity);
                _pipeline = new EventIngestPipeline(_queueCapacity, _duplicateWindow, _clock);
                _engine = new DiagnosticEngine(_dataProvider, _log, _clock);
                _dispatcher = new NormalizedEventDispatcher();
                _engine.Register(new Core.Engine.Probes.M1EnvironmentProbe());
                _engine.Register(new Core.Engine.Probes.M2ConnectionProbe());

                _log.Write(now, LogLevel.Information, string.Empty, _session.SessionId,
                    "Runtime initialized. queueCapacity=" + _queueCapacity + " duplicateWindow=" + _duplicateWindow);
            }
        }

        /// <summary>
        /// Starts the background drain worker. The worker is the only place where heavy
        /// processing happens, keeping host callbacks free.
        /// </summary>
        public void Start()
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    throw new ObjectDisposedException(nameof(DiagnosticRuntime));
                }

                if (_session == null)
                {
                    throw new InvalidOperationException("Initialize must be called before Start.");
                }

                if (_running)
                {
                    return;
                }

                _running = true;
                _worker = new Thread(DrainLoop)
                {
                    IsBackground = true,
                    Name = "RiskPilot.Diagnostics.Drain"
                };
                _worker.Start();
                _log.Write(_clock.UtcNow, LogLevel.Information, string.Empty, _session.SessionId, "Runtime started.");
            }
        }

        /// <summary>
        /// Stops accepting new work and joins the drain worker within a bounded time so
        /// shutdown cannot hang the host.
        /// </summary>
        public void Stop()
        {
            Thread worker;
            lock (_gate)
            {
                if (!_running)
                {
                    return;
                }

                _running = false;
                worker = _worker;
                _worker = null;
            }

            if (worker != null && worker.IsAlive)
            {
                worker.Join(TimeSpan.FromSeconds(5));
            }

            var session = Session;
            if (session != null && _log != null)
            {
                _log.Write(_clock.UtcNow, LogLevel.Information, string.Empty, session.SessionId,
                    "Runtime stopped. processed=" + session.ProcessedCount + " dropped=" + session.DroppedCount);
            }
        }

        /// <summary>
        /// Enqueues a normalized record for asynchronous processing. Called from host
        /// callback threads; never blocks and never takes a lock. The pipeline and session
        /// references are published before Start and only replaced under Dispose, so a
        /// volatile read is sufficient and the callback path never contends on the runtime
        /// gate. Returns false when the record was dropped or suppressed as a duplicate,
        /// or when the runtime is not initialized.
        /// </summary>
        public bool Ingest(Core.Models.NormalizedRecord record)
        {
            var pipeline = _pipeline;
            var session = _session;
            if (pipeline == null || session == null)
            {
                return false;
            }

            return pipeline.Ingest(session, record);
        }

        /// <summary>Runs a diagnostic pass and returns the report. Never throws.</summary>
        public DiagnosticReport RunDiagnosticPass()
        {
            DiagnosticEngine engine;
            SessionContext session;
            lock (_gate)
            {
                engine = _engine;
                session = _session;
            }

            if (engine == null || session == null)
            {
                var empty = new DiagnosticReport { CorrelationId = CorrelationId.New(), GeneratedAtUtc = _clock.UtcNow };
                empty.Add(Core.Result.DiagnosticResult
                    .Inconclusive("M0", "M0.NOT_INITIALIZED", empty.CorrelationId, _clock.UtcNow)
                    .WithReasonCode("RUNTIME_NOT_INITIALIZED"));
                return empty;
            }

            try
            {
                return engine.Run(session);
            }
            catch (Exception ex)
            {
                var report = new DiagnosticReport { SessionId = session.SessionId, GeneratedAtUtc = _clock.UtcNow };
                report.Add(Core.Result.DiagnosticResult
                    .Fail("M0", "M0.ENGINE_EXCEPTION", ErrorClassifier.Classify(ex), report.CorrelationId, _clock.UtcNow)
                    .WithReasonCode(ErrorClassifier.Classify(ex).ToWire())
                    .WithEvidence(Core.Result.DiagnosticEvidence.Observable("DiagnosticEngine", ErrorClassifier.Describe(ex), _clock.UtcNow)));
                return report;
            }
        }

        /// <summary>
        /// Writes the JSON report plus a SHA-256 manifest to the supplied sink. Returns
        /// the report JSON. Throwing sinks are contained so the host is never disrupted.
        /// </summary>
        public string WriteReport(IReportSink sink)
        {
            var report = RunDiagnosticPass();
            var json = ReportSerializer.Serialize(report);
            if (sink == null)
            {
                return json;
            }

            var manifest = new ReportManifest
            {
                SessionId = report.SessionId,
                CorrelationId = report.CorrelationId,
                GeneratedAtUtc = report.GeneratedAtUtc,
                AggregateStatus = report.AggregateStatus()
            };
            manifest.AddEntry("report.json", json);
            manifest.ComputeManifestHash();

            try
            {
                sink.Write("report.json", json);
                sink.Write("manifest.json", ManifestSerializer.Serialize(manifest));
            }
            catch (Exception ex)
            {
                if (_log != null)
                {
                    _log.Write(_clock.UtcNow, LogLevel.Error, report.CorrelationId, report.SessionId,
                        "Report write failed: " + ErrorClassifier.Describe(ex));
                }
            }

            return json;
        }

        private void DrainLoop()
        {
            var buffer = new Core.Models.NormalizedRecord[256];
            while (_running)
            {
                var drained = _pipeline.Queue.Drain(buffer, buffer.Length);
                if (drained == 0)
                {
                    Thread.Sleep(10);
                    continue;
                }

                for (var i = 0; i < drained; i++)
                {
                    Process(buffer[i]);
                    buffer[i] = null;
                }
            }

            // Final drain so shutdown never abandons queued records silently.
            int tail;
            do
            {
                tail = _pipeline.Queue.Drain(buffer, buffer.Length);
                for (var i = 0; i < tail; i++)
                {
                    Process(buffer[i]);
                    buffer[i] = null;
                }
            }
            while (tail > 0);
        }

        private void Process(Core.Models.NormalizedRecord record)
        {
            if (record == null)
            {
                return;
            }

            _session.RecordProcessed();

            // Hand the payload to registered consumers (future M3–M8 modules). Phase 2
            // ships no consumers, so this is a no-op that establishes the extension point.
            var dispatcher = _dispatcher;
            if (dispatcher != null)
            {
                dispatcher.Dispatch(record);
            }
        }

        public void Dispose()
        {
            Thread worker;
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                _running = false;
                worker = _worker;
                _worker = null;
            }

            if (worker != null && worker.IsAlive)
            {
                worker.Join(TimeSpan.FromSeconds(5));
            }

            lock (_gate)
            {
                _disposed = true;
            }
        }
    }

    /// <summary>Destination for report artifacts (file system on the host, memory in tests).</summary>
    public interface IReportSink
    {
        void Write(string name, string content);
    }
}
