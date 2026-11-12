using System;
using System.Collections.Generic;

namespace RiskPilot.Diagnostics.Nt8.Core.Run
{
    /// <summary>
    /// Per-run context handed to an <see cref="IDiagnosticRunner"/>. It carries the module
    /// list for the selected mode, a cancellation probe and progress callbacks. The runner
    /// must call the callbacks from a single worker thread and must never touch a UI type.
    /// </summary>
    public sealed class DiagnosticRunContext
    {
        private readonly Action<string> _onModuleStarted;
        private readonly Action<ModuleStatusView> _onModuleCompleted;
        private readonly Action<string, string> _onSession;

        public DiagnosticRunContext(
            DiagnosticMode mode,
            IReadOnlyList<string> moduleIds,
            Func<bool> isCancelled,
            Action<string> onModuleStarted,
            Action<ModuleStatusView> onModuleCompleted,
            Action<string, string> onSession)
        {
            Mode = mode;
            ModuleIds = moduleIds ?? new string[0];
            IsCancelled = isCancelled ?? (() => false);
            _onModuleStarted = onModuleStarted;
            _onModuleCompleted = onModuleCompleted;
            _onSession = onSession;
        }

        public DiagnosticMode Mode { get; private set; }

        public IReadOnlyList<string> ModuleIds { get; private set; }

        /// <summary>Returns true once a cancel has been requested; checked between modules.</summary>
        public Func<bool> IsCancelled { get; private set; }

        public void ModuleStarted(string moduleId)
        {
            if (_onModuleStarted != null)
            {
                _onModuleStarted(moduleId);
            }
        }

        public void ModuleCompleted(ModuleStatusView view)
        {
            if (_onModuleCompleted != null && view != null)
            {
                _onModuleCompleted(view);
            }
        }

        public void Session(string sessionId, string correlationId)
        {
            if (_onSession != null)
            {
                _onSession(sessionId, correlationId);
            }
        }
    }

    /// <summary>
    /// Executes one diagnostic pass for a mode. Implemented in the host layer over the
    /// diagnostic runtime; a test double implements it synchronously. The runner owns the
    /// "how" of the pass; the coordinator owns the "when" and the single-run guarantee.
    /// </summary>
    public interface IDiagnosticRunner
    {
        /// <summary>
        /// Runs the mode described by <paramref name="context"/> and returns the result.
        /// Must honour <see cref="DiagnosticRunContext.IsCancelled"/> between modules and must
        /// never throw to signal a normal cancellation.
        /// </summary>
        DiagnosticRunResult Run(DiagnosticRunContext context);
    }
}
