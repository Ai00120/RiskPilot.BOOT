using System;
using RiskPilot.Diagnostics.Nt8.Core.Clock;
using RiskPilot.Diagnostics.Nt8.Core.Session;

namespace RiskPilot.Diagnostics.Nt8.Core.Engine
{
    /// <summary>
    /// Everything a probe needs for one evaluation pass: the read-only data provider, the
    /// session, the clock, and the correlation id. Immutable for the duration of a pass.
    /// </summary>
    public sealed class ProbeContext
    {
        public ProbeContext(
            IDiagnosticDataProvider dataProvider,
            SessionContext session,
            IClock clock,
            string correlationId)
        {
            DataProvider = dataProvider ?? throw new ArgumentNullException(nameof(dataProvider));
            Session = session ?? throw new ArgumentNullException(nameof(session));
            Clock = clock ?? SystemClock.Instance;
            CorrelationId = correlationId ?? Core.Identity.CorrelationId.New();
        }

        public IDiagnosticDataProvider DataProvider { get; private set; }

        public SessionContext Session { get; private set; }

        public IClock Clock { get; private set; }

        public string CorrelationId { get; private set; }
    }
}
