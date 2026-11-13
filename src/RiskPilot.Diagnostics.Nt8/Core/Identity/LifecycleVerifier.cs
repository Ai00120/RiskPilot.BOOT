using System;
using System.Collections.Generic;
using RiskPilot.Diagnostics.Nt8.Core.Models.Observations;
using RiskPilot.Diagnostics.Nt8.Core.Status;

namespace RiskPilot.Diagnostics.Nt8.Core.Identity
{
    /// <summary>
    /// The lifecycle phases M1 reports. They mirror the NinjaTrader <c>State</c> progression an
    /// AddOn observes through <c>OnStateChange</c>, grouped so a reader sees where in the lifecycle
    /// the host actually is.
    /// <para>
    /// <see cref="Connection"/> is a phase of the <em>host</em>, not of the AddOn lifecycle: no
    /// NinjaTrader <c>State</c> member carries it, so it is never inferred here. M1 reports it as
    /// deferred to M2 (the connection/provider owner) rather than duplicating M2's evidence.
    /// </para>
    /// </summary>
    public static class LifecyclePhases
    {
        public const string Initialization = "INITIALIZATION";
        public const string Startup = "STARTUP";
        public const string Connection = "CONNECTION";
        public const string Runtime = "RUNTIME";
        public const string Shutdown = "SHUTDOWN";
        public const string Unknown = "UNKNOWN";

        /// <summary>
        /// Maps a raw <c>State</c> member name to its phase, or <see cref="Unknown"/> when the
        /// member is not one M1 recognises. The mapping is a closed switch: an unrecognised member
        /// is never guessed into a phase.
        /// </summary>
        public static string PhaseOf(string state)
        {
            switch (state)
            {
                case "SetDefaults":
                case "Configure":
                    return Initialization;
                case "Active":
                    return Startup;
                case "DataLoaded":
                case "Historical":
                case "Transition":
                case "Realtime":
                    return Runtime;
                case "Terminated":
                    return Shutdown;
                default:
                    return Unknown;
            }
        }

        /// <summary>
        /// The canonical NinjaTrader <c>State</c> order, used to detect an out-of-order
        /// transition. Unknown members return -1 so they never participate in the ordering check
        /// (they are handled as an explicit unknown state instead).
        /// </summary>
        public static int RankOf(string state)
        {
            switch (state)
            {
                case "SetDefaults":
                    return 0;
                case "Configure":
                    return 1;
                case "Active":
                    return 2;
                case "DataLoaded":
                    return 3;
                case "Historical":
                    return 4;
                case "Transition":
                    return 5;
                case "Realtime":
                    return 6;
                case "Terminated":
                    return 7;
                default:
                    return -1;
            }
        }

        /// <summary>True when the raw state name is a recognised NinjaTrader lifecycle member.</summary>
        public static bool IsRecognized(string state)
        {
            return RankOf(state) >= 0;
        }
    }

    /// <summary>Stable reason codes emitted by the lifecycle verifier.</summary>
    public static class LifecycleReasonCodes
    {
        public const string Valid = "LIFECYCLE_VALID";
        public const string Unobservable = "LIFECYCLE_UNOBSERVABLE";
        public const string StubOrMock = "LIFECYCLE_VALUE_FROM_STUB_OR_MOCK";
        public const string NotObserved = "LIFECYCLE_NOT_OBSERVED";
        public const string UnknownState = "LIFECYCLE_UNKNOWN_STATE";
        public const string OutOfOrder = "LIFECYCLE_TRANSITION_OUT_OF_ORDER";
        public const string StateAfterTermination = "LIFECYCLE_STATE_AFTER_TERMINATION";
        public const string InitializationNotObserved = "LIFECYCLE_INITIALIZATION_NOT_OBSERVED";
        public const string StartupNotObserved = "LIFECYCLE_STARTUP_NOT_OBSERVED";
    }

    /// <summary>
    /// Outcome of the lifecycle evaluation. It carries only the status, the reason codes and the
    /// classified phase flags — never a raw host value beyond the state member names the host
    /// itself reported.
    /// </summary>
    public sealed class LifecycleVerdict
    {
        public DiagnosticStatus Status { get; private set; }

        public IList<string> ReasonCodes { get; private set; }

        /// <summary>The raw states the host reported, in order.</summary>
        public IList<string> ObservedStates { get; private set; }

        /// <summary>Observed states that M1 could not classify into a phase.</summary>
        public IList<string> UnknownStates { get; private set; }

        public bool InitializationObserved { get; private set; }

        public bool StartupObserved { get; private set; }

        public bool RuntimeObserved { get; private set; }

        public bool ShutdownObserved { get; private set; }

        /// <summary>Number of observed transitions (== number of states reported).</summary>
        public int TransitionCount { get; private set; }

        /// <summary>The last recognised state the host reported, or empty.</summary>
        public string CurrentState { get; private set; }

        /// <summary>
        /// Always false: no NinjaTrader <c>State</c> member carries a connection phase, so the
        /// lifecycle surface never proves it. Connection is M2's responsibility.
        /// </summary>
        public bool ConnectionPhaseObservable { get { return false; } }

        private LifecycleVerdict()
        {
            ReasonCodes = new List<string>();
            ObservedStates = new List<string>();
            UnknownStates = new List<string>();
            CurrentState = string.Empty;
        }

        internal static LifecycleVerdict Create(
            DiagnosticStatus status,
            IList<string> observedStates,
            IList<string> unknownStates,
            IList<string> reasonCodes,
            bool initializationObserved,
            bool startupObserved,
            bool runtimeObserved,
            bool shutdownObserved,
            string currentState)
        {
            return new LifecycleVerdict
            {
                Status = status,
                ObservedStates = new List<string>(observedStates ?? new List<string>()),
                UnknownStates = new List<string>(unknownStates ?? new List<string>()),
                ReasonCodes = new List<string>(reasonCodes ?? new List<string>()),
                InitializationObserved = initializationObserved,
                StartupObserved = startupObserved,
                RuntimeObserved = runtimeObserved,
                ShutdownObserved = shutdownObserved,
                TransitionCount = observedStates != null ? observedStates.Count : 0,
                CurrentState = currentState ?? string.Empty
            };
        }
    }

    /// <summary>
    /// Classifies and validates the observed AddOn lifecycle transitions. The evaluation is
    /// fail-closed and never guesses:
    /// <list type="bullet">
    /// <item>a missing or unobservable lifecycle surface is INCONCLUSIVE;</item>
    /// <item>a stub/mock lifecycle value is INCONCLUSIVE (it cannot establish the real lifecycle);</item>
    /// <item>an unrecognised state member is INCONCLUSIVE (it cannot be classified);</item>
    /// <item>a transition that moves backwards in the canonical order is FAIL (abnormal);</item>
    /// <item>a state observed after <c>Terminated</c> is FAIL (abnormal);</item>
    /// <item>a lifecycle whose initialization or startup was not observed is INCONCLUSIVE (it
    /// cannot be proven to have started correctly);</item>
    /// <item>only an ordered lifecycle that reached startup is PASS. Shutdown is optional — the
    /// diagnostic normally runs while the AddOn is live — but if observed it must be last.</item>
    /// </list>
    /// </summary>
    public static class LifecycleVerifier
    {
        public static LifecycleVerdict Evaluate(LifecycleObservation observation)
        {
            if (observation == null || !observation.LifecycleSurfaceObservable)
            {
                return Inconclusive(observation, LifecycleReasonCodes.Unobservable);
            }

            if (observation.IsStubOrMockValue)
            {
                return Inconclusive(observation, LifecycleReasonCodes.StubOrMock);
            }

            var states = observation.ObservedStates ?? new List<string>();
            if (states.Count == 0)
            {
                return Inconclusive(observation, LifecycleReasonCodes.NotObserved);
            }

            var unknown = new List<string>();
            var initialization = false;
            var startup = false;
            var runtime = false;
            var shutdown = false;
            var current = string.Empty;

            var previousRank = -1;
            var terminatedSeen = false;
            var outOfOrder = false;
            var stateAfterTermination = false;

            foreach (var state in states)
            {
                var phase = LifecyclePhases.PhaseOf(state);
                if (phase == LifecyclePhases.Unknown)
                {
                    if (!unknown.Contains(state))
                    {
                        unknown.Add(state);
                    }

                    continue;
                }

                if (terminatedSeen)
                {
                    // Any further transition after Terminated is abnormal (the AddOn is being torn
                    // down; nothing should follow it).
                    stateAfterTermination = true;
                }

                switch (phase)
                {
                    case LifecyclePhases.Initialization:
                        initialization = true;
                        break;
                    case LifecyclePhases.Startup:
                        startup = true;
                        break;
                    case LifecyclePhases.Runtime:
                        runtime = true;
                        break;
                    case LifecyclePhases.Shutdown:
                        shutdown = true;
                        terminatedSeen = true;
                        break;
                }

                var rank = LifecyclePhases.RankOf(state);
                // A backwards move (rank decrease) is an abnormal transition. Equal ranks are
                // allowed: SetDefaults legitimately fires more than once.
                if (rank < previousRank)
                {
                    outOfOrder = true;
                }

                previousRank = rank;
                current = state;
            }

            if (stateAfterTermination)
            {
                return Create(
                    DiagnosticStatus.FAIL, states, unknown,
                    new List<string> { LifecycleReasonCodes.StateAfterTermination },
                    initialization, startup, runtime, shutdown, current);
            }

            if (outOfOrder)
            {
                return Create(
                    DiagnosticStatus.FAIL, states, unknown,
                    new List<string> { LifecycleReasonCodes.OutOfOrder },
                    initialization, startup, runtime, shutdown, current);
            }

            if (unknown.Count > 0)
            {
                return Create(
                    DiagnosticStatus.INCONCLUSIVE, states, unknown,
                    new List<string> { LifecycleReasonCodes.UnknownState },
                    initialization, startup, runtime, shutdown, current);
            }

            if (!initialization)
            {
                return Create(
                    DiagnosticStatus.INCONCLUSIVE, states, unknown,
                    new List<string> { LifecycleReasonCodes.InitializationNotObserved },
                    initialization, startup, runtime, shutdown, current);
            }

            if (!startup)
            {
                return Create(
                    DiagnosticStatus.INCONCLUSIVE, states, unknown,
                    new List<string> { LifecycleReasonCodes.StartupNotObserved },
                    initialization, startup, runtime, shutdown, current);
            }

            return Create(
                DiagnosticStatus.PASS, states, unknown,
                new List<string> { LifecycleReasonCodes.Valid },
                initialization, startup, runtime, shutdown, current);
        }

        private static LifecycleVerdict Inconclusive(LifecycleObservation observation, string reasonCode)
        {
            var states = observation != null && observation.ObservedStates != null
                ? observation.ObservedStates
                : new List<string>();

            return Create(
                DiagnosticStatus.INCONCLUSIVE, states, new List<string>(),
                new List<string> { reasonCode },
                false, false, false, false, string.Empty);
        }

        private static LifecycleVerdict Create(
            DiagnosticStatus status,
            IList<string> states,
            IList<string> unknown,
            IList<string> reasons,
            bool initialization,
            bool startup,
            bool runtime,
            bool shutdown,
            string current)
        {
            return LifecycleVerdict.Create(status, states, unknown, reasons, initialization, startup, runtime, shutdown, current);
        }
    }
}
