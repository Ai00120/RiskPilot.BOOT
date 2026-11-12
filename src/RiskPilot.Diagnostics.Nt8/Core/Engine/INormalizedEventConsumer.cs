using RiskPilot.Diagnostics.Nt8.Core.Models;

namespace RiskPilot.Diagnostics.Nt8.Core.Engine
{
    /// <summary>
    /// Consumer of normalized events, invoked on the runtime's drain thread (never on a
    /// NinjaTrader callback thread). This is the extension point for future diagnostic
    /// modules (M3–M8): a module registers a consumer instead of touching the ingest path.
    /// <para>
    /// Contract:
    /// <list type="bullet">
    /// <item>A consumer runs on the single drain thread; implementations must be
    /// thread-safe with respect to reads of shared session state.</item>
    /// <item>A consumer must not block indefinitely. Heavy work is acceptable here
    /// precisely because this is off the callback path.</item>
    /// <item>A consumer must not throw to signal a normal condition; the runtime contains
    /// exceptions and records them as an error rather than letting one consumer abort the
    /// drain loop.</item>
    /// <item>A consumer is strictly read-only: it must never submit, change, cancel or
    /// close an order, nor mutate an account, connection or position.</item>
    /// </list>
    /// </para>
    /// </summary>
    public interface INormalizedEventConsumer
    {
        /// <summary>Stable name used in diagnostics and error reporting.</summary>
        string Name { get; }

        /// <summary>Consumes one normalized record. Must not throw to signal normal conditions.</summary>
        void Consume(NormalizedRecord record);
    }
}
