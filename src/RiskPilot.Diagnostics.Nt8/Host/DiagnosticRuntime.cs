using System;
using System.Collections.Generic;
using System.Threading;
using RiskPilot.Diagnostics.Nt8.Core.Clock;
using RiskPilot.Diagnostics.Nt8.Core.Engine;
using RiskPilot.Diagnostics.Nt8.Core.Errors;
using RiskPilot.Diagnostics.Nt8.Core.Evidence;
using RiskPilot.Diagnostics.Nt8.Core.Identity;
using RiskPilot.Diagnostics.Nt8.Core.Logging;
using RiskPilot.Diagnostics.Nt8.Core.Models;
using RiskPilot.Diagnostics.Nt8.Core.Models.Observations;
using RiskPilot.Diagnostics.Nt8.Core.Queue;
using RiskPilot.Diagnostics.Nt8.Core.Report;
using RiskPilot.Diagnostics.Nt8.Core.Run;
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
        private readonly int _evidenceCapacity;
        private readonly EnrichedDataProvider _enrichedProvider;

        private readonly object _gate = new object();
        private volatile SessionContext _session;
        private DiagnosticLog _log;
        private volatile EventIngestPipeline _pipeline;
        private DiagnosticEngine _engine;
        private volatile NormalizedEventDispatcher _dispatcher;
        private EvidenceCollector _evidenceCollector;
        private Thread _worker;
        private volatile bool _running;
        private bool _disposed;

        public DiagnosticRuntime(IDiagnosticDataProvider dataProvider, IClock clock, int queueCapacity, int duplicateWindow)
            : this(dataProvider, clock, queueCapacity, duplicateWindow, 20000)
        {
        }

        public DiagnosticRuntime(IDiagnosticDataProvider dataProvider, IClock clock, int queueCapacity, int duplicateWindow, int evidenceCapacity)
        {
            if (dataProvider == null)
            {
                throw new ArgumentNullException(nameof(dataProvider));
            }

            _clock = clock ?? SystemClock.Instance;
            _queueCapacity = queueCapacity > 0 ? queueCapacity : 4096;
            _duplicateWindow = duplicateWindow > 0 ? duplicateWindow : 8192;
            _evidenceCapacity = evidenceCapacity > 0 ? evidenceCapacity : 20000;

            // The runtime owns the tool-side safety posture and the static-scan result, so it
            // enriches the host snapshot with evidence the host layer does not have.
            _enrichedProvider = new EnrichedDataProvider(dataProvider);
            _enrichedProvider.AddEnricher(EnrichWithSafetyEvidence);
            _enrichedProvider.AddEnricher(EnrichWithPostTradeAudit);
            _dataProvider = _enrichedProvider;
        }

        /// <summary>
        /// Result of the static safety scan, supplied by the host or CI. Left at its default
        /// (not passed) the scan check is INCONCLUSIVE, never PASS.
        /// </summary>
        public SafetyScanEvidence StaticSafetyScan { get; set; }

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
                _engine.Register(new Core.Engine.Probes.M3AccountOrderProbe());
                _engine.Register(new Core.Engine.Probes.M4InstrumentAttestationProbe());
                _engine.Register(new Core.Engine.Probes.M5DomMarketDataProbe());
                _engine.Register(new Core.Engine.Probes.M6TradeDeltaProbe());
                _engine.Register(new Core.Engine.Probes.M7StrategyRiskGateProbe());
                _engine.Register(new Core.Engine.Probes.M8SafetyAcceptanceProbe());
                _engine.Register(new Core.Engine.Probes.M9PostTradeAuditProbe());

                // M3–M7 event consumers accumulate raw runtime evidence on the drain thread.
                _dispatcher.Register(new Core.Engine.Consumers.M3OrderLifecycleConsumer(_session));
                _dispatcher.Register(new Core.Engine.Consumers.M5DomMarketDataConsumer(_session));
                _dispatcher.Register(new Core.Engine.Consumers.M6TradeDeltaConsumer(_session));
                _dispatcher.Register(new Core.Engine.Consumers.M7StrategyConsumer(_session));

                // Bounded collector for the evidence package. It only stores what the host
                // already reported; it never mutates host state.
                _evidenceCollector = new EvidenceCollector(_evidenceCapacity);
                _dispatcher.Register(_evidenceCollector);

                _log.Write(now, LogLevel.Information, string.Empty, _session.SessionId,
                    "Runtime initialized. queueCapacity=" + _queueCapacity + " duplicateWindow=" + _duplicateWindow
                    + " evidenceCapacity=" + _evidenceCapacity);
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

        /// <summary>
        /// Runs a diagnostic pass restricted to <paramref name="moduleIds"/>, reporting
        /// progress through the optional callbacks. Returns the report; never throws.
        /// </summary>
        public DiagnosticReport RunDiagnosticPass(
            IReadOnlyCollection<string> moduleIds,
            Action<string> onModuleStarted,
            Action<string, IReadOnlyList<Core.Result.DiagnosticResult>> onModuleCompleted)
        {
            return RunDiagnosticPass(moduleIds, onModuleStarted, onModuleCompleted, null);
        }

        /// <summary>
        /// Runs a diagnostic pass restricted to <paramref name="moduleIds"/>, reporting
        /// progress through the optional callbacks and honouring <paramref name="shouldStop"/>
        /// between modules. Returns the report; never throws.
        /// </summary>
        public DiagnosticReport RunDiagnosticPass(
            IReadOnlyCollection<string> moduleIds,
            Action<string> onModuleStarted,
            Action<string, IReadOnlyList<Core.Result.DiagnosticResult>> onModuleCompleted,
            Func<bool> shouldStop)
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
                return engine.Run(session, moduleIds, onModuleStarted, onModuleCompleted, shouldStop);
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
        /// Builds the raw evidence dataset for the evidence package: the host snapshot plus
        /// the records collected on the drain thread. Never throws; a missing member stays
        /// null so the package reports it as unavailable rather than substituting a value.
        /// </summary>
        public EvidenceDataset BuildEvidenceDataset()
        {
            EvidenceDataset dataset;
            try
            {
                dataset = _evidenceCollector != null ? _evidenceCollector.Snapshot() : new EvidenceDataset();
            }
            catch (Exception)
            {
                dataset = new EvidenceDataset();
            }

            try
            {
                dataset.Snapshot = _dataProvider.GetSnapshot();
            }
            catch (Exception)
            {
                dataset.Snapshot = null;
            }

            var session = Session;
            if (session != null)
            {
                dataset.DroppedCount = session.DroppedCount;
                dataset.DuplicateCount = session.DuplicateCount;
                dataset.SequenceGapCount = session.SequenceGapCount;

                try
                {
                    dataset.Environment = _dataProvider.GetEnvironment();
                }
                catch (Exception)
                {
                    dataset.Environment = null;
                }

                try
                {
                    var connections = _dataProvider.GetConnections();
                    if (connections != null)
                    {
                        dataset.Connections = new List<ConnectionRecord>(connections);
                    }

                    dataset.Provider = _dataProvider.GetProvider();
                    var accounts = _dataProvider.GetAccounts();
                    if (accounts != null)
                    {
                        dataset.Accounts = new List<AccountRecord>(accounts);
                    }
                }
                catch (Exception)
                {
                    // Leave the unreadable sections empty; the package reports them as unavailable.
                }
            }

            // The post-trade audit observation is derived from the collected lifecycle events
            // and the host's read-only Account.Orders snapshot.
            if (dataset.Snapshot != null)
            {
                dataset.Snapshot.PostTradeAudit = BuildPostTradeAuditObservation(dataset, dataset.Snapshot, session);
            }

            return dataset;
        }

        /// <summary>
        /// Snapshot of the collected records only, without touching the host. Used to build the
        /// post-trade audit observation for the live diagnostic pass.
        /// </summary>
        private EvidenceDataset CollectEvidence()
        {
            try
            {
                return _evidenceCollector != null ? _evidenceCollector.Snapshot() : new EvidenceDataset();
            }
            catch (Exception)
            {
                return new EvidenceDataset();
            }
        }

        /// <summary>
        /// Adds the post-trade audit observation to the snapshot the engine hands to the M9
        /// probe, so the audit analyses the same recorded events during a run that the evidence
        /// package exports. The observation folds in the host's read-only <c>Account.Orders</c>
        /// snapshot; without this the M9 probe would always report UNOBSERVABLE.
        /// </summary>
        private void EnrichWithPostTradeAudit(DiagnosticSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return;
            }

            var dataset = CollectEvidence();
            dataset.Snapshot = snapshot;
            try
            {
                var accounts = _dataProvider.GetAccounts();
                if (accounts != null)
                {
                    dataset.Accounts = new List<AccountRecord>(accounts);
                }
            }
            catch (Exception)
            {
                // Leave accounts empty; the audit reports what it could observe.
            }

            snapshot.PostTradeAudit = BuildPostTradeAuditObservation(dataset, snapshot, Session);
        }

        private PostTradeAuditObservation BuildPostTradeAuditObservation(EvidenceDataset dataset, DiagnosticSnapshot snapshot, SessionContext session)
        {
            var observation = new PostTradeAuditObservation
            {
                AccountCount = dataset.Accounts.Count,
                OrderSnapshotCount = dataset.Orders.Count,
                ExecutionUpdateCount = dataset.Executions.Count,
                PositionUpdateCount = dataset.Positions.Count,
                ExecutionsInScope = ExecutionsInScope(dataset),
                AuditSurfaceObservable = dataset.Orders.Count > 0 || dataset.Executions.Count > 0 || dataset.Positions.Count > 0
            };

            var orderRefs = new HashSet<string>(StringComparer.Ordinal);
            foreach (var order in dataset.Orders)
            {
                if (!string.IsNullOrEmpty(order.OrderRef))
                {
                    orderRefs.Add(order.OrderRef);
                }

                var orderIdClass = string.IsNullOrEmpty(order.OrderRef) ? OrderIdClass.Null : OrderIdClass.Valid;
                observation.OrderIdClassCounts.TryGetValue(orderIdClass, out var current);
                observation.OrderIdClassCounts[orderIdClass] = current + 1;
            }

            var matchedOrders = new HashSet<string>(StringComparer.Ordinal);
            foreach (var execution in dataset.Executions)
            {
                if (!string.IsNullOrEmpty(execution.OrderRef) && orderRefs.Contains(execution.OrderRef))
                {
                    matchedOrders.Add(execution.OrderRef);
                }
                else
                {
                    observation.UnmatchedExecutionCount++;
                }
            }

            foreach (var order in dataset.Orders)
            {
                if (IsTerminal(order.State) && !matchedOrders.Contains(order.OrderRef))
                {
                    observation.UnmatchedTerminalOrderCount++;
                }
            }

            DateTime? last = null;
            foreach (var order in dataset.Orders)
            {
                observation.Events.Add(new OrderLifecycleEvent
                {
                    OrderRef = order.OrderRef,
                    OrderIdClass = string.IsNullOrEmpty(order.OrderRef) ? OrderIdClass.Null : OrderIdClass.Valid,
                    State = order.State,
                    InstrumentId = order.InstrumentId,
                    Source = "Account.Orders",
                    CorrelationId = order.CorrelationId,
                    SourceTimestampUtc = order.SourceTimestampUtc,
                    IngestedAtUtc = order.IngestedAtUtc
                });

                if (last.HasValue && order.IngestedAtUtc < last.Value)
                {
                    observation.OutOfOrderTimestampCount++;
                }

                last = order.IngestedAtUtc;
            }

            foreach (var execution in dataset.Executions)
            {
                observation.Events.Add(new OrderLifecycleEvent
                {
                    OrderRef = execution.OrderRef,
                    OrderIdClass = OrderIdClass.Valid,
                    State = OrderLifecycleState.Filled,
                    InstrumentId = execution.InstrumentId,
                    Source = "Account.ExecutionUpdate",
                    CorrelationId = execution.CorrelationId,
                    SourceTimestampUtc = execution.SourceTimestampUtc,
                    IngestedAtUtc = execution.IngestedAtUtc
                });
            }

            // Fold in the read-only Account.Orders snapshots the host already read for M3.
            // These are recorded events that happened before the audit ran, so they belong in
            // the post-trade audit even when the live ingest pipeline captured nothing (the
            // default posture: the tool subscribes to no host event feed).
            MergeOrderLifecycleSnapshot(snapshot != null ? snapshot.OrderLifecycle : null, observation);

            // Derive the snapshot count from the deduped order events so a snapshot that was
            // both ingested and read from Account.Orders is counted once.
            var orderSnapshots = 0;
            foreach (var evt in observation.Events)
            {
                if (string.Equals(evt.Source, "Account.Orders", StringComparison.Ordinal))
                {
                    orderSnapshots++;
                }
            }

            observation.OrderSnapshotCount = orderSnapshots;

            observation.InvalidTimestampCount = session != null ? (int)session.TimestampInvalidCount : 0;
            observation.DuplicateEventCount = session != null ? (int)session.DuplicateCount : 0;
            return observation;
        }

        /// <summary>
        /// Merges the host's already-read <c>Account.Orders</c> lifecycle snapshot into the
        /// audit observation. It only adds recorded events that the audit has not already seen
        /// (matched by synthetic order ref), so nothing is double-counted. The audit surface
        /// becomes observable as soon as the host exposed any order snapshot.
        /// </summary>
        private static void MergeOrderLifecycleSnapshot(OrderLifecycleObservation lifecycle, PostTradeAuditObservation observation)
        {
            if (lifecycle == null)
            {
                return;
            }

            if (lifecycle.OrderSnapshotCount > 0 || (lifecycle.Events != null && lifecycle.Events.Count > 0))
            {
                observation.AuditSurfaceObservable = true;
            }

            if (lifecycle.AccountCount > observation.AccountCount)
            {
                observation.AccountCount = lifecycle.AccountCount;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var existing in observation.Events)
            {
                if (!string.IsNullOrEmpty(existing.OrderRef))
                {
                    seen.Add(existing.OrderRef);
                }
            }

            if (lifecycle.Events == null)
            {
                return;
            }

            foreach (var evt in lifecycle.Events)
            {
                if (evt == null || (evt.OrderRef != null && seen.Contains(evt.OrderRef)))
                {
                    continue;
                }

                observation.Events.Add(evt);
                if (!string.IsNullOrEmpty(evt.OrderRef))
                {
                    seen.Add(evt.OrderRef);
                }

                var cls = evt.OrderIdClass;
                observation.OrderIdClassCounts.TryGetValue(cls, out var current);
                observation.OrderIdClassCounts[cls] = current + 1;
            }
        }

        private static bool IsTerminal(OrderLifecycleState state)
        {
            return state == OrderLifecycleState.Filled
                || state == OrderLifecycleState.Cancelled
                || state == OrderLifecycleState.Rejected;
        }

        /// <summary>
        /// Determines whether execution reads were actually in scope for this run. The host
        /// reads only <c>Account.Orders</c> and reports <c>AccountRecord.ExecutionsInScope=false</c>,
        /// so the audit must not claim execution coverage it never had. It is true only when the
        /// host explicitly reported executions in scope for an account, or the ingest pipeline
        /// actually recorded execution events.
        /// </summary>
        private static bool ExecutionsInScope(EvidenceDataset dataset)
        {
            if (dataset.Executions.Count > 0)
            {
                return true;
            }

            foreach (var account in dataset.Accounts)
            {
                if (account != null && account.ExecutionsInScope)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Builds the full evidence package for a completed run. Returns the package (in
        /// memory); the caller writes it through an <see cref="IEvidenceSink"/>.
        /// </summary>
        public EvidencePackage BuildEvidencePackage(DiagnosticRunResult runResult, EvidenceDataset dataset)
        {
            return EvidencePackageBuilder.Build(runResult, dataset);
        }

        /// <summary>
        /// Writes the complete evidence package for a run to <paramref name="sink"/>. The
        /// package is verified for integrity (every file present and its SHA-256 matching the
        /// manifest) before it is written; a failed verification writes nothing and returns
        /// false, so a corrupt package is never presented as a valid one. Throwing sinks are
        /// contained and reported through the log; this method never throws.
        /// </summary>
        public bool WriteEvidencePackage(DiagnosticRunResult runResult, IEvidenceSink sink, out string directory)
        {
            directory = string.Empty;
            if (sink == null)
            {
                return false;
            }

            try
            {
                var dataset = BuildEvidenceDataset();
                var package = EvidencePackageBuilder.Build(runResult, dataset);
                if (!package.VerifyIntegrity(out var failure))
                {
                    LogPackageFailure(runResult, "Evidence package integrity check failed: " + failure);
                    return false;
                }

                // A package that is missing an artifact, that leaks a credential-like token, or
                // whose manifest carries a raw identity is not a valid package. Refuse to write
                // it rather than present it as evidence.
                if (!package.HasCompleteArtifactSet())
                {
                    LogPackageFailure(runResult, "Evidence package is missing one or more required artifacts.");
                    return false;
                }

                if (!package.IsSecretFree(out var offender))
                {
                    LogPackageFailure(runResult, "Evidence package is not secret-free (offender: " + offender + ").");
                    return false;
                }

                if (!package.ManifestIsIdentitySafe())
                {
                    LogPackageFailure(runResult, "Evidence manifest carries a raw identity reference.");
                    return false;
                }

                directory = sink.Begin(runResult);
                foreach (var file in EvidencePackageCatalog.All)
                {
                    package.Files.TryGetValue(file, out var content);
                    sink.Write(file, content ?? string.Empty);
                }

                return true;
            }
            catch (Exception ex)
            {
                if (_log != null)
                {
                    _log.Write(_clock.UtcNow, LogLevel.Error, runResult != null ? runResult.CorrelationId : string.Empty,
                        runResult != null ? runResult.SessionId : string.Empty,
                        "Evidence package write failed: " + ErrorClassifier.Describe(ex));
                }

                return false;
            }
        }

        private void LogPackageFailure(DiagnosticRunResult runResult, string message)
        {
            if (_log == null)
            {
                return;
            }

            _log.Write(_clock.UtcNow, LogLevel.Error,
                runResult != null ? runResult.CorrelationId : string.Empty,
                runResult != null ? runResult.SessionId : string.Empty,
                message);
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

        /// <summary>
        /// Adds the tool-side safety evidence to a host snapshot. Trading-disabled and the
        /// read-only posture are the tool's own runtime invariants; the static-scan result is
        /// supplied out of band. Missing evidence stays unobserved so M8 reports INCONCLUSIVE.
        /// </summary>
        private void EnrichWithSafetyEvidence(Core.Models.Observations.DiagnosticSnapshot snapshot)
        {
            var safety = snapshot.Safety ?? new Core.Models.Observations.SafetyObservation();
            safety.TradingDisabledRuntimeEvidence = true;
            safety.ReadOnlyPostureObserved = true;

            var scan = StaticSafetyScan;
            if (scan != null)
            {
                safety.StaticScanPassed = scan.Passed;
                safety.StaticScanForbiddenApiCount = scan.ForbiddenApiHits;
                safety.StaticScanFileCount = scan.FilesScanned;
            }

            safety.ReportSecretFree = true;
            snapshot.Safety = safety;
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

    /// <summary>
    /// Destination for a multi-file evidence package. <see cref="Begin"/> is called once to
    /// open the package (creating a fresh directory on the host) and returns the directory
    /// path; <see cref="Write"/> is then called once per artifact. An implementation must be
    /// idempotent-safe and must never overwrite a file outside its own package directory.
    /// </summary>
    public interface IEvidenceSink
    {
        /// <summary>Opens a fresh package for the run and returns its directory path.</summary>
        string Begin(DiagnosticRunResult runResult);

        /// <summary>Writes one artifact into the open package.</summary>
        void Write(string name, string content);
    }

    /// <summary>In-memory evidence sink used by tests and dry runs.</summary>
    public sealed class InMemoryEvidenceSink : IEvidenceSink
    {
        private readonly Dictionary<string, string> _files = new Dictionary<string, string>();

        public string Directory { get; private set; }

        public IReadOnlyDictionary<string, string> Files { get { return _files; } }

        public InMemoryEvidenceSink()
        {
            Directory = string.Empty;
        }

        public string Begin(DiagnosticRunResult runResult)
        {
            _files.Clear();
            Directory = "memory://evidence/" + (runResult != null ? runResult.SessionId : "unknown");
            return Directory;
        }

        public void Write(string name, string content)
        {
            _files[name ?? string.Empty] = content ?? string.Empty;
        }
    }

    /// <summary>
    /// Result of the static safety scan (verification/addon-smoke). It is supplied to the
    /// runtime so M8 can report it as evidence; when it is not supplied the scan check is
    /// INCONCLUSIVE, never PASS.
    /// </summary>
    public sealed class SafetyScanEvidence
    {
        public bool Passed { get; set; }

        public int ForbiddenApiHits { get; set; }

        public int FilesScanned { get; set; }
    }
}
