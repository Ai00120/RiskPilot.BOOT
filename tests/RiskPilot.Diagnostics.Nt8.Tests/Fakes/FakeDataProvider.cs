using System.Collections.Generic;
using RiskPilot.Diagnostics.Nt8.Core.Engine;
using RiskPilot.Diagnostics.Nt8.Core.Models;

namespace RiskPilot.Diagnostics.Nt8.Tests.Fakes
{
    /// <summary>
    /// In-memory implementation of the read-only data provider. It substitutes only for
    /// the NinjaTrader host boundary; all diagnostic logic under test is real production
    /// code.
    /// </summary>
    public sealed class FakeDataProvider : IDiagnosticDataProvider
    {
        public EnvironmentRecord Environment { get; set; }

        public List<ConnectionRecord> Connections { get; private set; }

        public ProviderRecord Provider { get; set; }

        public List<AccountRecord> Accounts { get; private set; }

        public Dictionary<string, InstrumentRecord> Instruments { get; private set; }

        public List<string> PresentApis { get; private set; }

        public List<string> MissingApis { get; private set; }

        public FakeDataProvider()
        {
            Environment = new EnvironmentRecord { Environment = EnvironmentClass.Simulation };
            Connections = new List<ConnectionRecord>();
            Accounts = new List<AccountRecord>();
            Instruments = new Dictionary<string, InstrumentRecord>();
            PresentApis = new List<string> { "NinjaTrader.Cbi.Connection", "NinjaTrader.Cbi.Account" };
            MissingApis = new List<string>();
        }

        public EnvironmentRecord GetEnvironment() { return Environment; }

        public IReadOnlyList<ConnectionRecord> GetConnections() { return Connections; }

        public ProviderRecord GetProvider() { return Provider; }

        public IReadOnlyList<AccountRecord> GetAccounts() { return Accounts; }

        public InstrumentRecord GetInstrument(string instrumentId)
        {
            if (instrumentId != null && Instruments.TryGetValue(instrumentId, out var record))
            {
                return record;
            }

            return null;
        }

        public IReadOnlyList<string> GetPresentApis() { return PresentApis; }

        public IReadOnlyList<string> GetMissingApis() { return MissingApis; }
    }
}
