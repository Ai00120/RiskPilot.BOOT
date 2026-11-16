namespace RiskPilot.Diagnostics.Nt8.Core.Identity
{
    /// <summary>
    /// Classifies the raw NinjaTrader <c>State</c> member an AddOn observes through
    /// <c>OnStateChange</c> into the two facts a host-readiness decision needs: whether the AddOn
    /// is <em>operational</em> (initialised and able to read host objects) and whether it is
    /// shutting down.
    /// <para>
    /// This is the authoritative fix for the AddOn readiness bug. Per the official NinjaTrader 8
    /// documentation an AddOn uses the <b>"Active state"</b> management system: it progresses
    /// <c>SetDefaults -&gt; Configure -&gt; Active -&gt; Terminated</c> and <b>never</b> reaches the
    /// data-processing states <c>DataLoaded</c>/<c>Historical</c>/<c>Transition</c>/<c>Realtime</c>
    /// (those belong to Indicators, Strategies, Market Analyzer columns and SuperDOM columns). The
    /// previous readiness probe only accepted the data-processing states, so it was permanently
    /// false for an AddOn and no host metadata, instrument or market data could ever be observed.
    /// </para>
    /// <para>
    /// The data-processing states are still recognised as operational: an AddOn never reports them,
    /// but a host that does (or a shared enum value) is operational rather than unknown. A
    /// <c>null</c>/empty or unrecognised state is never treated as operational (fail closed).
    /// </para>
    /// </summary>
    public static class AddOnLifecycle
    {
        /// <summary>The AddOn operational state: initialised and able to read host objects.</summary>
        public const string Active = "Active";

        /// <summary>The AddOn shutdown state.</summary>
        public const string Terminated = "Terminated";

        /// <summary>
        /// True when <paramref name="state"/> means the AddOn is operational and host objects may be
        /// read. This is the corrected readiness predicate: it accepts the AddOn's own
        /// <c>Active</c> state (the state an AddOn actually reaches) as well as the data-processing
        /// states (which an AddOn does not reach, but which are operational if a host reports one).
        /// A null/empty/unknown state is false.
        /// </summary>
        public static bool IsOperational(string state)
        {
            switch (state)
            {
                case Active:
                case "DataLoaded":
                case "Historical":
                case "Transition":
                case "Realtime":
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>True when the state name is the AddOn shutdown state.</summary>
        public static bool IsShutdown(string state)
        {
            return string.Equals(state, Terminated, System.StringComparison.Ordinal);
        }

        /// <summary>
        /// True when the state name is a recognised NinjaTrader <c>State</c> member. An unrecognised
        /// member is never treated as operational (fail closed).
        /// </summary>
        public static bool IsRecognized(string state)
        {
            switch (state)
            {
                case "SetDefaults":
                case "Configure":
                case Active:
                case "DataLoaded":
                case "Historical":
                case "Transition":
                case "Realtime":
                case Terminated:
                    return true;
                default:
                    return false;
            }
        }
    }
}
