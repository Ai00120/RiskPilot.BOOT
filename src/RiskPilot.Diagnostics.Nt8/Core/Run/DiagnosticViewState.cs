using System;
using System.Collections.Generic;
using RiskPilot.Diagnostics.Nt8.Core.Identity;
using RiskPilot.Diagnostics.Nt8.Core.Status;

namespace RiskPilot.Diagnostics.Nt8.Core.Run
{
    /// <summary>
    /// Observable state for the Diagnostic Center window. It holds the selected mode, the
    /// live progress, the per-module rows, the last result and the three-subject acceptance
    /// summary, and raises <see cref="Changed"/> whenever any of them changes.
    /// <para>
    /// All access is guarded by a single lock and the exposed collections are copies, so a
    /// UI thread reading the state never observes a partially-updated run. Update methods
    /// are safe to call from the run worker; the host marshals <see cref="Changed"/> onto the
    /// dispatcher. A shutting-down host simply stops receiving notifications.
    /// </para>
    /// </summary>
    public sealed class DiagnosticViewState
    {
        private readonly object _gate = new object();
        private DiagnosticMode _selectedMode = DiagnosticMode.Quick;
        private DiagnosticRunProgress _progress = DiagnosticRunProgress.Idle;
        private DiagnosticRunResult _result;
        private readonly List<ModuleStatusView> _modules = new List<ModuleStatusView>();
        private IReadOnlyList<AcceptanceSubjectResult> _acceptance = new List<AcceptanceSubjectResult>();
        private string _statusLine = "Trading: DISABLED | Not run";

        /// <summary>Raised after any state change. Handlers must not block.</summary>
        public event Action Changed;

        public DiagnosticMode SelectedMode
        {
            get { lock (_gate) { return _selectedMode; } }
            set
            {
                lock (_gate)
                {
                    if (_selectedMode == value)
                    {
                        return;
                    }

                    _selectedMode = value;
                }

                Raise();
            }
        }

        public IReadOnlyList<DiagnosticModeDefinition> Modes { get { return DiagnosticModeCatalog.All; } }

        public DiagnosticRunProgress Progress { get { lock (_gate) { return _progress; } } }

        public DiagnosticRunResult Result { get { lock (_gate) { return _result; } } }

        public IReadOnlyList<ModuleStatusView> Modules
        {
            get { lock (_gate) { return new List<ModuleStatusView>(_modules); } }
        }

        public IReadOnlyList<AcceptanceSubjectResult> Acceptance
        {
            get { lock (_gate) { return _acceptance; } }
        }

        public string StatusLine { get { lock (_gate) { return _statusLine; } } }

        public bool IsRunning { get { lock (_gate) { return _progress.State == RunState.Running || _progress.State == RunState.Cancelling; } } }

        public bool CanCancel { get { lock (_gate) { return _progress.State == RunState.Running; } } }

        public bool CanExport
        {
            get
            {
                lock (_gate)
                {
                    return !IsRunningInternal() && _result != null && !string.IsNullOrEmpty(_result.EvidenceDirectory);
                }
            }
        }

        /// <summary>Resets the module grid and progress for a new run.</summary>
        public void BeginRun(DiagnosticMode mode)
        {
            lock (_gate)
            {
                _selectedMode = mode;
                _modules.Clear();
                _result = null;
                _acceptance = new List<AcceptanceSubjectResult>();
                _progress = new DiagnosticRunProgress(RunState.Running, DiagnosticModeCatalog.For(mode).Id, string.Empty, 0,
                    DiagnosticModeCatalog.For(mode).ModuleIds.Count, DateTime.UtcNow, null);
                _statusLine = "Trading: DISABLED | Running " + DiagnosticModeCatalog.For(mode).Title + "...";
            }

            Raise();
        }

        /// <summary>Applies a progress update from the coordinator (already on the UI thread).</summary>
        public void ApplyProgress(DiagnosticRunProgress progress)
        {
            if (progress == null)
            {
                return;
            }

            lock (_gate)
            {
                _progress = progress;
                _statusLine = "Trading: DISABLED | " + progress.State + " " + progress.Percent + "%"
                    + (string.IsNullOrEmpty(progress.CurrentModuleId) ? string.Empty : " (" + progress.CurrentModuleId + ")");
            }

            Raise();
        }

        /// <summary>Records a completed module row.</summary>
        public void ApplyModule(ModuleStatusView module)
        {
            if (module == null)
            {
                return;
            }

            lock (_gate)
            {
                for (var i = 0; i < _modules.Count; i++)
                {
                    if (_modules[i].ModuleId == module.ModuleId)
                    {
                        _modules[i] = module;
                        Raise();
                        return;
                    }
                }

                _modules.Add(module);
            }

            Raise();
        }

        /// <summary>Applies a finished run: fills the module rows and the acceptance summary.</summary>
        public void ApplyResult(DiagnosticRunResult result, bool stubBuild)
        {
            if (result == null)
            {
                return;
            }

            var acceptance = AcceptanceSummaryBuilder.Build(result, stubBuild);
            lock (_gate)
            {
                _result = result;
                if (result.Modules != null && result.Modules.Count > 0)
                {
                    _modules.Clear();
                    _modules.AddRange(result.Modules);
                }

                _acceptance = acceptance;
                _progress = new DiagnosticRunProgress(result.FinalState, result.ModeId, string.Empty,
                    _modules.Count, _modules.Count, result.StartedAtUtc, result.FinishedAtUtc);
                _statusLine = "Trading: DISABLED | " + result.FinalState + " | " + result.AggregateStatus.ToWire();
            }

            Raise();
        }

        /// <summary>Marks the evidence package as written.</summary>
        public void ApplyEvidenceDirectory(string directory)
        {
            lock (_gate)
            {
                if (_result == null)
                {
                    return;
                }

                _result.EvidenceDirectory = directory ?? string.Empty;
            }

            Raise();
        }

        public void SetStatusLine(string text)
        {
            lock (_gate)
            {
                _statusLine = text ?? string.Empty;
            }

            Raise();
        }

        private bool IsRunningInternal()
        {
            return _progress.State == RunState.Running || _progress.State == RunState.Cancelling;
        }

        private void Raise()
        {
            var handler = Changed;
            if (handler != null)
            {
                handler();
            }
        }
    }
}
