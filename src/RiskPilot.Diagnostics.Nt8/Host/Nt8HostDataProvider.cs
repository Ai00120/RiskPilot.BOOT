using System;
using System.Collections.Generic;
using NinjaTrader.Cbi;
using RiskPilot.Diagnostics.Nt8.Core.Engine;
using RiskPilot.Diagnostics.Nt8.Core.Models;
using RiskPilot.Diagnostics.Nt8.Core.Security;

namespace RiskPilot.Diagnostics.Nt8.Host
{
    /// <summary>
    /// The ONLY class that reads NinjaTrader types. It implements the read-only
    /// <see cref="IDiagnosticDataProvider"/> against documented public APIs. It never
    /// submits, changes or cancels an order, never mutates an account or connection, and
    /// never uses reflection. Provider identity is resolved by string comparison because
    /// the provider enum has no guaranteed Rithmic member.
    /// </summary>
    public sealed class Nt8HostDataProvider : IDiagnosticDataProvider
    {
        private static readonly string[] RequiredApis =
        {
            "NinjaTrader.NinjaScript.AddOnBase",
            "NinjaTrader.Cbi.Connection",
            "NinjaTrader.Cbi.ConnectOptions",
            "NinjaTrader.Cbi.Account",
            "NinjaTrader.Cbi.Instrument",
            "NinjaTrader.Data.MarketData",
            "NinjaTrader.Data.MarketDepth"
        };

        private readonly bool _masterMetadataReady;

        public Nt8HostDataProvider(bool masterMetadataReady)
        {
            _masterMetadataReady = masterMetadataReady;
        }

        public EnvironmentRecord GetEnvironment()
        {
            var record = new EnvironmentRecord
            {
                Environment = DetectEnvironment(),
                ToolBuildMode = Core.Identity.BuildIdentity.BuildMode,
                TargetFramework = DetectTargetFramework(),
                UtcOffsetMinutes = (int)TimeZoneInfo.Local.GetUtcOffset(DateTime.UtcNow).TotalMinutes,
                SourceTimeBasis = SourceTimeBasis.ServerTimeUnverified,
                IngestedAtUtc = DateTime.UtcNow
            };

            // No public API exposes the exact host build; leave it unobservable.
            record.HostVersionObservable = false;
            record.HostVersion = string.Empty;
            return record;
        }

        public IReadOnlyList<ConnectionRecord> GetConnections()
        {
            var list = new List<ConnectionRecord>();
            var connections = Connection.Connections;
            if (connections == null)
            {
                return list;
            }

            lock (connections)
            {
                foreach (var connection in connections)
                {
                    if (connection == null || connection.Options == null)
                    {
                        continue;
                    }

                    var providerName = connection.Options.Provider.ToString();
                    list.Add(new ConnectionRecord
                    {
                        ConnectionName = connection.Options.Name,
                        ProviderName = providerName,
                        ProviderDetectedByString = true,
                        PriceState = MapConnectionState(connection.PriceStatus),
                        OrderChannelState = MapConnectionState(connection.Status),
                        Role = ConnectionRole.Broker,
                        IngestedAtUtc = DateTime.UtcNow
                    });
                }
            }

            return list;
        }

        public ProviderRecord GetProvider()
        {
            var connections = Connection.Connections;
            if (connections == null)
            {
                return null;
            }

            lock (connections)
            {
                foreach (var connection in connections)
                {
                    if (connection == null || connection.Options == null)
                    {
                        continue;
                    }

                    var name = connection.Options.Provider.ToString();
                    return new ProviderRecord
                    {
                        Name = name,
                        ResolutionMethod = "String",
                        IsExpectedEvidenceProvider = string.Equals(
                            name,
                            Core.Engine.Probes.M2ConnectionProbe.ExpectedProvider,
                            StringComparison.OrdinalIgnoreCase),
                        ProviderBuildObservable = false,
                        IngestedAtUtc = DateTime.UtcNow
                    };
                }
            }

            return null;
        }

        public IReadOnlyList<AccountRecord> GetAccounts()
        {
            var list = new List<AccountRecord>();
            var accounts = Account.All;
            if (accounts == null)
            {
                return list;
            }

            lock (accounts)
            {
                foreach (var account in accounts)
                {
                    if (account == null)
                    {
                        continue;
                    }

                    list.Add(new AccountRecord
                    {
                        AccountRef = SyntheticRef.For("account", account.Name),
                        AccountClass = ClassifyAccount(account.Name),
                        Denomination = account.Denomination.ToString(),
                        ProviderName = account.Provider.ToString(),
                        PositionCount = account.Positions == null ? 0 : account.Positions.Count,
                        WorkingOrderCount = 0,
                        ExecutionsInScope = false,
                        IngestedAtUtc = DateTime.UtcNow
                    });
                }
            }

            return list;
        }

        public InstrumentRecord GetInstrument(string instrumentId)
        {
            if (string.IsNullOrEmpty(instrumentId))
            {
                return null;
            }

            var instrument = Instrument.GetInstrument(instrumentId);
            if (instrument == null)
            {
                return null;
            }

            var record = new InstrumentRecord
            {
                FullName = instrument.FullName,
                InstrumentId = instrumentId,
                Expiry = instrument.Expiry.ToString("yyyy-MM-dd"),
                Exchange = instrument.Exchange.ToString(),
                MasterMetadataAvailable = false,
                IngestedAtUtc = DateTime.UtcNow
            };

            // Master-instrument metadata must not be read before DataLoaded; guard it.
            if (_masterMetadataReady && instrument.MasterInstrument != null)
            {
                try
                {
                    record.RootSymbol = instrument.MasterInstrument.Name;
                    record.InstrumentType = instrument.MasterInstrument.InstrumentType.ToString();
                    record.Currency = instrument.MasterInstrument.Currency.ToString();
                    record.TickSize = instrument.MasterInstrument.TickSize;
                    record.PointValue = instrument.MasterInstrument.PointValue;
                    record.MasterMetadataAvailable = true;
                }
                catch (Exception)
                {
                    record.MasterMetadataAvailable = false;
                }
            }

            return record;
        }

        public IReadOnlyList<string> GetPresentApis()
        {
            // These APIs are present by construction: the code compiles only when the
            // referenced public types exist on the host.
            return new List<string>(RequiredApis);
        }

        public IReadOnlyList<string> GetMissingApis()
        {
            return new List<string>();
        }

        private static EnvironmentClass DetectEnvironment()
        {
            var accounts = Account.All;
            if (accounts == null)
            {
                return EnvironmentClass.Unknown;
            }

            lock (accounts)
            {
                foreach (var account in accounts)
                {
                    if (account == null || string.IsNullOrEmpty(account.Name))
                    {
                        continue;
                    }

                    return ClassifyAccount(account.Name);
                }
            }

            return EnvironmentClass.Unknown;
        }

        private static EnvironmentClass ClassifyAccount(string accountName)
        {
            if (string.IsNullOrEmpty(accountName))
            {
                return EnvironmentClass.Unknown;
            }

            return accountName.StartsWith("Sim", StringComparison.OrdinalIgnoreCase)
                ? EnvironmentClass.Simulation
                : EnvironmentClass.Live;
        }

        private static ConnectionState MapConnectionState(ConnectionStatus status)
        {
            switch (status)
            {
                case ConnectionStatus.Connected:
                    return ConnectionState.Connected;
                case ConnectionStatus.Connecting:
                    return ConnectionState.Connecting;
                case ConnectionStatus.ConnectionLost:
                    return ConnectionState.ConnectionLost;
                case ConnectionStatus.Disconnected:
                    return ConnectionState.Disconnected;
                default:
                    return ConnectionState.Unknown;
            }
        }

        private static string DetectTargetFramework()
        {
#if USE_STUBS
            return "net8.0";
#else
            return "net48";
#endif
        }
    }
}
