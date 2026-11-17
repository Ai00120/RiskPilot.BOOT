using RiskPilot.Diagnostics.Nt8.Core.Models;

namespace RiskPilot.Diagnostics.Nt8.Core.Identity
{
    /// <summary>
    /// Host-free classification of the runtime environment class (Simulation vs Live) from the
    /// M2-verified target account's <em>own</em> connection mode.
    /// <para>
    /// The previous implementation returned the class of the <em>first</em> account enumerated in
    /// <c>Account.All</c> (a "Sim*" name-prefix heuristic), so M1 could PASS from an arbitrary,
    /// unrelated account. The environment class is a property of the connection mode, and the only
    /// account this tool is scoped to is the M2-verified target, so the class is derived from that
    /// account's connection and is <see cref="EnvironmentClass.Unknown"/> (INCONCLUSIVE) whenever
    /// the target is not verified or its mode cannot be read. It is never guessed from a name and
    /// never taken from an unverified account.
    /// </para>
    /// </summary>
    public static class EnvironmentClassifier
    {
        /// <summary>
        /// Classifies the environment from the verified target account's connection mode.
        /// Fail-closed: no verified target, or an unreadable mode, yields
        /// <see cref="EnvironmentClass.Unknown"/> (never a guess).
        /// </summary>
        /// <param name="targetAccountVerified">True only when the M2-verified target account was resolved.</param>
        /// <param name="connectionModeObservable">True when the target connection's mode was read.</param>
        /// <param name="connectionModeIsSimulation">The observed mode, when observable.</param>
        public static EnvironmentClass Classify(
            bool targetAccountVerified,
            bool connectionModeObservable,
            bool connectionModeIsSimulation)
        {
            if (!targetAccountVerified || !connectionModeObservable)
            {
                return EnvironmentClass.Unknown;
            }

            return connectionModeIsSimulation ? EnvironmentClass.Simulation : EnvironmentClass.Live;
        }
    }
}
