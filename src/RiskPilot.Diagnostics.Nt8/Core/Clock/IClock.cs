using System;

namespace RiskPilot.Diagnostics.Nt8.Core.Clock
{
    /// <summary>
    /// Abstraction over the wall clock so that timestamp handling is testable and
    /// deterministic. All diagnostic timestamps are expressed in UTC.
    /// </summary>
    public interface IClock
    {
        DateTime UtcNow { get; }
    }

    /// <summary>Default clock backed by <see cref="DateTime.UtcNow"/>.</summary>
    public sealed class SystemClock : IClock
    {
        public static readonly SystemClock Instance = new SystemClock();

        public DateTime UtcNow
        {
            get { return DateTime.UtcNow; }
        }
    }
}
