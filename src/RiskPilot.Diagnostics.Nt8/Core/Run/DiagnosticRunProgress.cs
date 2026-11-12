using System;

namespace RiskPilot.Diagnostics.Nt8.Core.Run
{
    /// <summary>
    /// Immutable, point-in-time progress of a diagnostic run. The coordinator publishes a new
    /// instance on every change (never mutates a published one), so a UI thread can read a
    /// consistent snapshot without locking.
    /// </summary>
    public sealed class DiagnosticRunProgress
    {
        public RunState State { get; private set; }

        public string ModeId { get; private set; }

        /// <summary>Module currently executing, or empty when none is.</summary>
        public string CurrentModuleId { get; private set; }

        public int CompletedModules { get; private set; }

        public int TotalModules { get; private set; }

        public DateTime StartedAtUtc { get; private set; }

        public DateTime? FinishedAtUtc { get; private set; }

        public DiagnosticRunProgress(
            RunState state,
            string modeId,
            string currentModuleId,
            int completedModules,
            int totalModules,
            DateTime startedAtUtc,
            DateTime? finishedAtUtc)
        {
            State = state;
            ModeId = modeId ?? string.Empty;
            CurrentModuleId = currentModuleId ?? string.Empty;
            CompletedModules = completedModules;
            TotalModules = totalModules;
            StartedAtUtc = startedAtUtc;
            FinishedAtUtc = finishedAtUtc;
        }

        public static DiagnosticRunProgress Idle
        {
            get { return new DiagnosticRunProgress(RunState.Idle, string.Empty, string.Empty, 0, 0, default(DateTime), null); }
        }

        /// <summary>Fraction complete in [0, 1]; zero when no run is active or the total is unknown.</summary>
        public double Fraction
        {
            get
            {
                if (TotalModules <= 0)
                {
                    return 0d;
                }

                var value = (double)CompletedModules / TotalModules;
                if (value < 0d) return 0d;
                if (value > 1d) return 1d;
                return value;
            }
        }

        public int Percent
        {
            get { return (int)Math.Round(Fraction * 100d); }
        }
    }
}
