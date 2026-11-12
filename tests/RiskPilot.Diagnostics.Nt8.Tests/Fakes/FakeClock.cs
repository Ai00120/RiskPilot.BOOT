using System;
using RiskPilot.Diagnostics.Nt8.Core.Clock;

namespace RiskPilot.Diagnostics.Nt8.Tests.Fakes
{
    /// <summary>
    /// Deterministic clock for tests. It is the only "fake" in the suite and it replaces
    /// nothing but the wall clock; every other collaborator is real production code.
    /// </summary>
    public sealed class FakeClock : IClock
    {
        private DateTime _now;

        public FakeClock(DateTime startUtc)
        {
            _now = DateTime.SpecifyKind(startUtc, DateTimeKind.Utc);
        }

        public DateTime UtcNow { get { return _now; } }

        public void Advance(TimeSpan delta)
        {
            _now = _now.Add(delta);
        }
    }
}
