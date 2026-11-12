using System;

namespace RiskPilot.Diagnostics.Nt8.Core.Run
{
    /// <summary>
    /// Lifecycle of a single diagnostic run. A run is a state machine; the invalid
    /// transitions (starting a run while one is active, completing a cancelled run) are
    /// rejected by <see cref="RunStateMachine"/> rather than silently tolerated.
    /// </summary>
    public enum RunState
    {
        Idle = 0,
        Running = 1,
        Cancelling = 2,
        Completed = 3,
        Cancelled = 4,
        Failed = 5
    }

    /// <summary>
    /// Guards the diagnostic run lifecycle so two runs can never execute concurrently and a
    /// cancel can never be mistaken for a completion. Every transition is a compare-and-set
    /// on the current state; an illegal transition returns false and leaves the state
    /// unchanged.
    /// </summary>
    public sealed class RunStateMachine
    {
        private readonly object _gate = new object();
        private RunState _state = RunState.Idle;

        public RunState State { get { lock (_gate) { return _state; } } }

        /// <summary>True while a run holds the single-run slot (Running or Cancelling).</summary>
        public bool IsActive
        {
            get
            {
                lock (_gate)
                {
                    return _state == RunState.Running || _state == RunState.Cancelling;
                }
            }
        }

        /// <summary>True when a new run may be started from the current state.</summary>
        public bool CanStart
        {
            get
            {
                lock (_gate)
                {
                    return _state == RunState.Idle
                        || _state == RunState.Completed
                        || _state == RunState.Cancelled
                        || _state == RunState.Failed;
                }
            }
        }

        /// <summary>
        /// Attempts to move Idle → Running. Returns false when a run is already active or
        /// the machine is in a cancelling state, which is how concurrent runs are prevented.
        /// </summary>
        public bool TryStart()
        {
            lock (_gate)
            {
                if (!(_state == RunState.Idle || _state == RunState.Completed || _state == RunState.Cancelled || _state == RunState.Failed))
                {
                    return false;
                }

                _state = RunState.Running;
                return true;
            }
        }

        /// <summary>Attempts Running → Cancelling. Returns false when no run is active.</summary>
        public bool TryRequestCancel()
        {
            lock (_gate)
            {
                if (_state != RunState.Running)
                {
                    return false;
                }

                _state = RunState.Cancelling;
                return true;
            }
        }

        /// <summary>Attempts (Running|Cancelling) → Cancelled.</summary>
        public bool TryCompleteCancelled()
        {
            lock (_gate)
            {
                if (_state != RunState.Cancelling && _state != RunState.Running)
                {
                    return false;
                }

                _state = RunState.Cancelled;
                return true;
            }
        }

        /// <summary>Attempts (Running|Cancelling) → Completed.</summary>
        public bool TryComplete()
        {
            lock (_gate)
            {
                if (_state != RunState.Running && _state != RunState.Cancelling)
                {
                    return false;
                }

                _state = RunState.Completed;
                return true;
            }
        }

        /// <summary>Attempts (Running|Cancelling) → Failed.</summary>
        public bool TryFail()
        {
            lock (_gate)
            {
                if (_state != RunState.Running && _state != RunState.Cancelling)
                {
                    return false;
                }

                _state = RunState.Failed;
                return true;
            }
        }

        /// <summary>Forces the machine back to Idle. Used only by a hard shutdown.</summary>
        public void Reset()
        {
            lock (_gate)
            {
                _state = RunState.Idle;
            }
        }
    }
}
