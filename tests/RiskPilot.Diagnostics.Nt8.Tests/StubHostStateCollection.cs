using Xunit;

namespace RiskPilot.Diagnostics.Nt8.Tests
{
    /// <summary>
    /// The host-adapter tests read and mutate the process-wide NinjaTrader stub collections
    /// (<c>Account.All</c>, <c>Connection.Connections</c>, <c>Instrument.All</c>). They must not run
    /// concurrently with one another, so they share a single non-parallel xunit collection.
    /// </summary>
    [CollectionDefinition("StubHostState", DisableParallelization = true)]
    public class StubHostStateCollection
    {
    }
}
