using System;
using System.Collections.Generic;
using NinjaTrader.Cbi;
using RiskPilot.Diagnostics.Nt8.Core.Engine;
using RiskPilot.Diagnostics.Nt8.Core.Identity;
using RiskPilot.Diagnostics.Nt8.Core.Instruments;
using RiskPilot.Diagnostics.Nt8.Core.Models;
using RiskPilot.Diagnostics.Nt8.Core.Models.Observations;
using RiskPilot.Diagnostics.Nt8.Core.Security;

namespace RiskPilot.Diagnostics.Nt8.Host
{
    /// <summary>
    /// The ONLY class that reads NinjaTrader types. It implements the read-only
    /// <see cref="IDiagnosticDataProvider"/> against documented public APIs. It never
    /// submits, changes or cancels an order, never mutates an account or connection, and
    /// never uses reflection. Provider identity is resolved by string comparison because
    /// the provider enum has no guaranteed Rithmic member (RiskPilot.BOT PR #275).
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
        private readonly string _requestedInstrumentId;

        public Nt8HostDataProvider(bool masterMetadataReady, string requestedInstrumentId = null)
        {
            _masterMetadataReady = masterMetadataReady;
            _requestedInstrumentId = requestedInstrumentId;
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

                    list.Add(new ConnectionRecord
                    {
                        ConnectionName = connection.Options.Name,
                        ProviderName = connection.Options.Provider.ToString(),
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

        /// <summary>
        /// Provider identity for the active connection. The provider is attributed to the target
        /// account's own connection when the target account is observable; otherwise it is read
        /// only from a single unambiguous connection. It is never taken from the first of several
        /// connections, so an unrelated provider cannot be reported as the evidence provider.
        /// </summary>
        public ProviderRecord GetProvider()
        {
            Account targetAccount;
            TryGetUniqueTargetAccount(out targetAccount);

            var connections = Connection.Connections;
            if (connections == null)
            {
                return null;
            }

            lock (connections)
            {
                var targetConnection = targetAccount != null ? targetAccount.Connection : null;
                if (targetConnection != null && targetConnection.Options != null)
                {
                    return BuildProviderRecord(targetConnection.Options.Provider.ToString());
                }

                // No target account connection is observable: only a single enumerated
                // connection is unambiguous enough to attribute the provider.
                Connection only = null;
                var count = 0;
                foreach (var connection in connections)
                {
                    if (connection == null || connection.Options == null)
                    {
                        continue;
                    }

                    count++;
                    only = connection;
                }

                return count == 1 && only != null
                    ? BuildProviderRecord(only.Options.Provider.ToString())
                    : null;
            }
        }

        private static ProviderRecord BuildProviderRecord(string providerName)
        {
            return new ProviderRecord
            {
                Name = providerName,
                ResolutionMethod = "String",
                IsExpectedEvidenceProvider = string.Equals(
                    providerName,
                    Core.Engine.Probes.M2ConnectionProbe.ExpectedProvider,
                    StringComparison.OrdinalIgnoreCase),
                ProviderBuildObservable = false,
                IngestedAtUtc = DateTime.UtcNow
            };
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
                        Denomination = SafeString(() => account.Denomination.ToString()),
                        ProviderName = SafeString(() => account.Provider.ToString()),
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
                Expiry = SafeString(() => instrument.Expiry.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)),
                Exchange = SafeString(() => instrument.Exchange.ToString()),
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

        /// <summary>
        /// Builds the read-only observation snapshot for M1–M8. Every member is filled only
        /// from documented public APIs; anything not observable is left null so the module
        /// reports INCONCLUSIVE. This method never throws for a normal "not available"
        /// condition and never mutates host state.
        /// </summary>
        public DiagnosticSnapshot GetSnapshot()
        {
            return new DiagnosticSnapshot
            {
                Runtime = BuildRuntimeObservation(),
                Provider = BuildProviderEvidence(),
                AccountAssociations = BuildAccountAssociations(),
                AccountIdentity = BuildAccountIdentityObservation(),
                TargetAccountConnection = BuildTargetAccountConnectionObservation(),
                OrderLifecycle = BuildOrderLifecycleObservation(),
                Attestation = BuildAttestationObservation(),
                DomMarketData = new DomMarketDataObservation(),
                TradeDelta = new TradeDeltaObservation(),
                StrategyRiskGate = new StrategyRiskGateObservation { StrategySurfaceObservable = false },
                Safety = new SafetyObservation()
            };
        }

        private RuntimeObservation BuildRuntimeObservation()
        {
            var is64 = SafeBool(() => Environment.Is64BitProcess);
            var observation = new RuntimeObservation
            {
                Is64BitProcess = is64,
                Is32BitProcess = is64 == true ? (bool?)false : null,
                ClrVersion = SafeString(() => Environment.Version.ToString()),
                OperatingSystem = SafeString(() => Environment.OSVersion.ToString()),
                HostVersionObservable = false
            };

            observation.Capabilities.Add(new RuntimeCapability("Account.Orders", CapabilityAvailability.Available, "Public read-only order snapshots."));
            observation.Capabilities.Add(new RuntimeCapability("Account.OrderUpdate", CapabilityAvailability.Available, "Public read-only order update events."));
            observation.Capabilities.Add(new RuntimeCapability("Instrument.MarketData", CapabilityAvailability.Available, "Public Level I market data."));
            observation.Capabilities.Add(new RuntimeCapability("Instrument.MarketDepth", CapabilityAvailability.Available, "Public Level II market depth."));
            observation.Capabilities.Add(new RuntimeCapability("HostVersion", CapabilityAvailability.Unobservable, "No public API exposes the exact host build."));
            observation.Capabilities.Add(new RuntimeCapability("ProviderBuild", CapabilityAvailability.Unobservable, "No public API exposes the provider build."));
            observation.Capabilities.Add(new RuntimeCapability("TradeAggressorSide", CapabilityAvailability.Unobservable, "Not derivable from public Level I data."));

            return observation;
        }

        private ProviderEvidence BuildProviderEvidence()
        {
            Account targetAccount;
            TryGetUniqueTargetAccount(out targetAccount);

            var connections = Connection.Connections;
            if (connections == null)
            {
                return null;
            }

            lock (connections)
            {
                var targetConnection = targetAccount != null ? targetAccount.Connection : null;
                if (targetConnection != null && targetConnection.Options != null)
                {
                    return BuildProviderEvidence(
                        targetConnection.Options.Provider.ToString(),
                        ProviderResolutionBasis.TargetAccountConnection);
                }

                // No target account connection is observable: only a single enumerated
                // connection is unambiguous enough to attribute the provider.
                Connection only = null;
                var count = 0;
                foreach (var connection in connections)
                {
                    if (connection == null || connection.Options == null)
                    {
                        continue;
                    }

                    count++;
                    only = connection;
                }

                return count == 1 && only != null
                    ? BuildProviderEvidence(only.Options.Provider.ToString(), ProviderResolutionBasis.SingleConnection)
                    : null;
            }
        }

        private static ProviderEvidence BuildProviderEvidence(string raw, string resolutionBasis)
        {
            return new ProviderEvidence
            {
                RawProviderValue = raw,
                ResolutionMethod = "String",
                ResolutionBasis = resolutionBasis,
                MatchesExpectedProvider = string.Equals(
                    raw,
                    Core.Engine.Probes.M2ConnectionProbe.ExpectedProvider,
                    StringComparison.OrdinalIgnoreCase),
                IsStubOrMockValue = false,
                ProviderBuildObservable = false
            };
        }

        private AccountIdentityObservation BuildAccountIdentityObservation()
        {
            var observation = new AccountIdentityObservation();
            var accounts = Account.All;
            if (accounts == null)
            {
                // The account surface itself could not be enumerated.
                observation.AccountSurfaceObservable = false;
                return observation;
            }

            observation.AccountSurfaceObservable = true;

            Account matched = null;
            var matchingCount = 0;
            lock (accounts)
            {
                foreach (var account in accounts)
                {
                    if (account == null || string.IsNullOrWhiteSpace(account.Name))
                    {
                        continue;
                    }

                    observation.ObservedAccountCount++;

                    // Exact ordinal comparison; a similar or differently-cased name is not a match.
                    if (ExpectedAccountIdentity.AccountNameMatches(account.Name))
                    {
                        matchingCount++;
                        matched = account;
                    }
                }
            }

            observation.MatchingAccountCount = matchingCount;

            // Only a unique exact match can be compared further; zero or many matches is
            // decided by the verifier, and no raw name is read here.
            if (matchingCount != 1 || matched == null)
            {
                return observation;
            }

            observation.AccountRef = SyntheticRef.For("account", matched.Name);

            var accountProvider = SafeString(() => matched.Provider.ToString());
            observation.AccountProviderObservable = !string.IsNullOrEmpty(accountProvider);
            observation.AccountProviderMatches = ExpectedAccountIdentity.ProviderMatches(accountProvider);

            var denomination = MapDenominationToIsoCode(matched.Denomination);
            observation.DenominationObservable = !string.IsNullOrEmpty(denomination);
            observation.DenominationIsUsd = ExpectedAccountIdentity.CurrencyIsUsd(denomination);

            var connection = matched.Connection;
            if (connection == null)
            {
                return observation;
            }

            observation.ConnectionObserved = true;

            var options = connection.Options;
            if (options != null)
            {
                var connectionName = SafeString(() => options.Name);
                observation.ConnectionNameObservable = !string.IsNullOrEmpty(connectionName);
                observation.ConnectionNameMatches = ExpectedAccountIdentity.ConnectionNameMatches(connectionName);

                var connectionProvider = SafeString(() => options.Provider.ToString());
                observation.ConnectionProviderObservable = !string.IsNullOrEmpty(connectionProvider);
                observation.ConnectionProviderMatches = ExpectedAccountIdentity.ProviderMatches(connectionProvider);

                var mode = SafeString(() => options.Mode.ToString());
                observation.ModeObservable = !string.IsNullOrEmpty(mode);
                observation.ModeIsSimulation = ExpectedAccountIdentity.ModeIsSimulation(mode);
            }

            var status = SafeString(() => connection.Status.ToString());
            observation.ConnectionStatusObservable = !string.IsNullOrEmpty(status);
            observation.ConnectionConnected = string.Equals(status, ConnectionStatus.Connected.ToString(), StringComparison.Ordinal);

            return observation;
        }

        /// <summary>
        /// Resolves the single account that matches the required account name exactly. Returns
        /// false (and a null account) when no account matched, more than one matched, or the
        /// account surface could not be enumerated, so callers never bind to an ambiguous or
        /// arbitrary account.
        /// </summary>
        private static bool TryGetUniqueTargetAccount(out Account target)
        {
            target = null;
            var accounts = Account.All;
            if (accounts == null)
            {
                return false;
            }

            var matchingCount = 0;
            lock (accounts)
            {
                foreach (var account in accounts)
                {
                    if (account == null || string.IsNullOrWhiteSpace(account.Name))
                    {
                        continue;
                    }

                    if (ExpectedAccountIdentity.AccountNameMatches(account.Name))
                    {
                        matchingCount++;
                        target = account;
                    }
                }
            }

            if (matchingCount != 1)
            {
                target = null;
                return false;
            }

            return true;
        }

        /// <summary>
        /// Builds the privacy-preserving observation that proves the target account is bound to
        /// the required connection and provider. It reads the target account's own
        /// <c>Account.Connection</c> (never the first entry of <c>Connection.Connections</c>) and
        /// also counts the enumerated connections and how many match the required connection
        /// identity, so a multiple-connection or ambiguous-association condition is detected. The
        /// raw account and connection names never enter the observation.
        /// </summary>
        private TargetAccountConnectionObservation BuildTargetAccountConnectionObservation()
        {
            var observation = new TargetAccountConnectionObservation();
            var accounts = Account.All;
            if (accounts == null)
            {
                observation.AccountSurfaceObservable = false;
                return observation;
            }

            observation.AccountSurfaceObservable = true;

            Account matched = null;
            var matchingCount = 0;
            lock (accounts)
            {
                foreach (var account in accounts)
                {
                    if (account == null || string.IsNullOrWhiteSpace(account.Name))
                    {
                        continue;
                    }

                    observation.ObservedAccountCount++;

                    if (ExpectedAccountIdentity.AccountNameMatches(account.Name))
                    {
                        matchingCount++;
                        matched = account;
                    }
                }
            }

            observation.MatchingAccountCount = matchingCount;

            var connections = Connection.Connections;
            if (connections != null)
            {
                lock (connections)
                {
                    foreach (var enumerated in connections)
                    {
                        if (enumerated == null || enumerated.Options == null)
                        {
                            continue;
                        }

                        observation.ConnectionCount++;

                        var connectionName = SafeString(() => enumerated.Options.Name);
                        var connectionProvider = SafeString(() => enumerated.Options.Provider.ToString());
                        if (ExpectedAccountIdentity.ConnectionNameMatches(connectionName)
                            && ExpectedAccountIdentity.ProviderMatches(connectionProvider))
                        {
                            observation.MatchingConnectionCount++;
                        }
                    }
                }
            }

            // Only a unique exact account match can be compared further; zero or many matches is
            // decided by the verifier, and no raw name is read here.
            if (matchingCount != 1 || matched == null)
            {
                return observation;
            }

            observation.AccountRef = SyntheticRef.For("account", matched.Name);

            var accountProvider = SafeString(() => matched.Provider.ToString());
            observation.AccountProviderObservable = !string.IsNullOrEmpty(accountProvider);
            observation.AccountProviderMatches = ExpectedAccountIdentity.ProviderMatches(accountProvider);

            var connection = matched.Connection;
            if (connection == null)
            {
                return observation;
            }

            observation.ConnectionObserved = true;

            var options = connection.Options;
            if (options != null)
            {
                var connectionName = SafeString(() => options.Name);
                observation.ConnectionNameObservable = !string.IsNullOrEmpty(connectionName);
                observation.ConnectionNameMatches = ExpectedAccountIdentity.ConnectionNameMatches(connectionName);

                var connectionProvider = SafeString(() => options.Provider.ToString());
                observation.ConnectionProviderObservable = !string.IsNullOrEmpty(connectionProvider);
                observation.ConnectionProviderMatches = ExpectedAccountIdentity.ProviderMatches(connectionProvider);
            }

            return observation;
        }

        private IList<AccountConnectionAssociation> BuildAccountAssociations()
        {
            var list = new List<AccountConnectionAssociation>();
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

                    var association = new AccountConnectionAssociation
                    {
                        AccountRef = SyntheticRef.For("account", account.Name),
                        AccountProvider = SafeString(() => account.Provider.ToString())
                    };

                    var connection = account.Connection;
                    if (connection != null && connection.Options != null)
                    {
                        association.HasConnection = true;
                        association.ConnectionName = connection.Options.Name;
                        association.ConnectionProvider = SafeString(() => connection.Options.Provider.ToString());
                        association.ProvidersAgree = string.Equals(
                            association.AccountProvider,
                            association.ConnectionProvider,
                            StringComparison.OrdinalIgnoreCase);
                    }

                    list.Add(association);
                }
            }

            return list;
        }

        private OrderLifecycleObservation BuildOrderLifecycleObservation()
        {
            var observation = new OrderLifecycleObservation();
            var accounts = Account.All;
            if (accounts == null)
            {
                observation.OrderSurfaceObservable = false;
                return observation;
            }

            lock (accounts)
            {
                foreach (var account in accounts)
                {
                    if (account == null)
                    {
                        continue;
                    }

                    observation.AccountCount++;
                    var orders = account.Orders;
                    if (orders == null)
                    {
                        continue;
                    }

                    foreach (var order in orders)
                    {
                        if (order == null)
                        {
                            continue;
                        }

                        observation.OrderSurfaceObservable = true;
                        observation.OrderSnapshotCount++;

                        var rawOrderId = SafeString(() => order.OrderId);
                        var orderIdClass = ClassifyOrderId(rawOrderId);
                        IncrementClass(observation.OrderIdClassCounts, orderIdClass);

                        var state = MapOrderState(order.OrderState);
                        if (state == OrderLifecycleState.Filled
                            || state == OrderLifecycleState.Cancelled
                            || state == OrderLifecycleState.Rejected)
                        {
                            observation.TerminalStateRetainedCount++;
                        }

                        var instrumentId = order.Instrument != null ? order.Instrument.FullName : string.Empty;
                        observation.Events.Add(new OrderLifecycleEvent
                        {
                            OrderRef = SyntheticRef.For("order", rawOrderId),
                            OrderIdClass = orderIdClass,
                            State = state,
                            InstrumentId = instrumentId,
                            Source = "Account.Orders",
                            CorrelationId = string.Empty,
                            IngestedAtUtc = DateTime.UtcNow
                        });
                    }
                }
            }

            return observation;
        }

        private InstrumentAttestationObservation BuildAttestationObservation()
        {
            var observation = new InstrumentAttestationObservation
            {
                RequestedInstrumentId = _requestedInstrumentId ?? string.Empty
            };

            if (string.IsNullOrEmpty(_requestedInstrumentId))
            {
                observation.HostAvailable = false;
                observation.FailureReasons.Add(Core.Engine.AttestationReasonCodes.HostUnavailable);
                return observation;
            }

            var instrument = Instrument.GetInstrument(_requestedInstrumentId);
            if (instrument == null)
            {
                observation.HostAvailable = false;
                observation.FailureReasons.Add(Core.Engine.AttestationReasonCodes.HostUnavailable);
                return observation;
            }

            var metadata = new InstrumentMetadataObservation
            {
                FullName = SafeString(() => instrument.FullName),
                ExpiryRaw = SafeString(() => instrument.Expiry.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)),
                ExpiryKind = SafeString(() => instrument.Expiry.Kind.ToString()),
                ExchangeRaw = SafeString(() => instrument.Exchange.ToString()),
                MasterMetadataAvailable = _masterMetadataReady
            };

            if (_masterMetadataReady && instrument.MasterInstrument != null)
            {
                metadata.RootName = SafeString(() => instrument.MasterInstrument.Name);
                metadata.InstrumentTypeRaw = SafeString(() => instrument.MasterInstrument.InstrumentType.ToString());
                metadata.CurrencyRaw = SafeString(() => instrument.MasterInstrument.Currency.ToString());
                metadata.PointValue = SafeDouble(() => instrument.MasterInstrument.PointValue);
                metadata.TickSize = SafeDouble(() => instrument.MasterInstrument.TickSize);
                if (metadata.PointValue.HasValue && metadata.TickSize.HasValue)
                {
                    metadata.TickValue = metadata.PointValue.Value * metadata.TickSize.Value;
                }
            }
            else
            {
                metadata.ReadFailures.Add("MasterInstrument");
            }

            observation.Metadata = metadata;
            observation.HostAvailable = true;

            // D-096 configured-route candidate: Globex selected and configured, Rithmic-only.
            observation.D096RouteCandidateSatisfied = IsD096RouteCandidate(instrument);
            var mappedExchange = observation.D096RouteCandidateSatisfied ? "CME" : null;
            observation.CanonicalInstrumentId = CanonicalInstrumentId.Build(metadata.FullName, mappedExchange);

            // Registry match and D-078 approval are resolved by the reviewed Owner-controlled
            // process. The tool only observes them; it never creates an approval.
            observation.RegistryMatched = string.Equals(
                observation.CanonicalInstrumentId,
                CanonicalInstrumentId.EvidenceIdentity,
                StringComparison.Ordinal);
            observation.D078Approved = false;

            if (!observation.RegistryMatched)
            {
                observation.FailureReasons.Add(Core.Engine.AttestationReasonCodes.MissingInstrumentSpecification);
            }

            observation.FailureReasons.Add(Core.Engine.AttestationReasonCodes.D078FieldApprovalNotApproved);
            return observation;
        }

        private static bool IsD096RouteCandidate(Instrument instrument)
        {
            try
            {
                var selectedGlobex = instrument.Exchange == Exchange.Globex;
                var master = instrument.MasterInstrument;
                var configuredOnlyGlobex = master != null
                    && master.Exchanges != null
                    && master.Exchanges.Count == 1
                    && master.Exchanges[0] == Exchange.Globex;
                return selectedGlobex && configuredOnlyGlobex && HasOnlyConnectedRithmicProvider();
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool HasOnlyConnectedRithmicProvider()
        {
            try
            {
                var connections = Connection.Connections;
                if (connections == null)
                {
                    return false;
                }

                lock (connections)
                {
                    var connectedCount = 0;
                    Connection only = null;
                    foreach (var connection in connections)
                    {
                        if (connection != null && connection.Status == ConnectionStatus.Connected)
                        {
                            connectedCount++;
                            only = connection;
                        }
                    }

                    return connectedCount == 1
                        && only != null
                        && only.Options != null
                        && string.Equals(only.Options.Provider.ToString(), "Rithmic", StringComparison.OrdinalIgnoreCase);
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static void IncrementClass(IDictionary<OrderIdClass, int> counts, OrderIdClass value)
        {
            counts.TryGetValue(value, out var current);
            counts[value] = current + 1;
        }

        /// <summary>
        /// Classifies a raw order id into NULL / EMPTY / WHITESPACE / VALID.
        /// </summary>
        private static OrderIdClass ClassifyOrderId(string orderId)
        {
            if (orderId == null)
            {
                return OrderIdClass.Null;
            }

            if (orderId.Length == 0)
            {
                return OrderIdClass.Empty;
            }

            for (var i = 0; i < orderId.Length; i++)
            {
                if (!char.IsWhiteSpace(orderId[i]))
                {
                    return OrderIdClass.Valid;
                }
            }

            return OrderIdClass.Whitespace;
        }

        private static OrderLifecycleState MapOrderState(OrderState state)
        {
            switch (state)
            {
                case OrderState.Initialized:
                    return OrderLifecycleState.Initialized;
                case OrderState.Submitted:
                    return OrderLifecycleState.Submitted;
                case OrderState.Accepted:
                    return OrderLifecycleState.Accepted;
                case OrderState.Working:
                    return OrderLifecycleState.Working;
                case OrderState.PartFilled:
                    return OrderLifecycleState.PartFilled;
                case OrderState.Filled:
                    return OrderLifecycleState.Filled;
                case OrderState.Cancelled:
                    return OrderLifecycleState.Cancelled;
                case OrderState.Rejected:
                    return OrderLifecycleState.Rejected;
                default:
                    return OrderLifecycleState.Unknown;
            }
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

        /// <summary>
        /// Maps the host currency enum to its ISO-4217 code so the required currency can be
        /// compared as an exact code. An unmapped member yields an empty string, which is treated
        /// as unobservable (fail closed) rather than guessed from the member name.
        /// </summary>
        private static string MapDenominationToIsoCode(Currency currency)
        {
            switch (currency)
            {
                case Currency.UsDollar:
                    return "USD";
                case Currency.Euro:
                    return "EUR";
                case Currency.Yen:
                    return "JPY";
                case Currency.BritishPound:
                    return "GBP";
                default:
                    return string.Empty;
            }
        }

        private static bool? SafeBool(Func<bool> reader)
        {
            try
            {
                return reader();
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static string SafeString(Func<string> reader)
        {
            try
            {
                return reader() ?? string.Empty;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        private static double? SafeDouble(Func<double> reader)
        {
            try
            {
                return reader();
            }
            catch (Exception)
            {
                return null;
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
