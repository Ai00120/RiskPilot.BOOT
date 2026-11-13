using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using NinjaTrader.Cbi;
using RiskPilot.Diagnostics.Nt8.Core.Contract;
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

        private readonly Func<bool> _masterMetadataReady;
        private readonly Func<IReadOnlyList<string>> _lifecycleStates;
        private readonly string _requestedInstrumentId;
        private readonly ISecurityScanSource _securityScanSource;

        public Nt8HostDataProvider(bool masterMetadataReady, string requestedInstrumentId = null)
            : this(() => masterMetadataReady, requestedInstrumentId)
        {
        }

        /// <summary>
        /// Creates the provider with a live readiness probe. <c>MasterInstrument.*</c> may only be
        /// read once the host has reached a data-processing state, so readiness is evaluated on each
        /// read rather than captured when the AddOn is constructed — an AddOn is constructed at
        /// <c>State.Active</c>, before that point, so a captured value would stay false forever.
        /// </summary>
        public Nt8HostDataProvider(Func<bool> masterMetadataReady, string requestedInstrumentId = null)
            : this(masterMetadataReady, null, requestedInstrumentId)
        {
        }

        /// <summary>
        /// Creates the provider with both host probes. <paramref name="lifecycleStates"/>, when
        /// supplied, is evaluated on each read to return the AddOn lifecycle transitions observed so
        /// far (in order). It is a probe rather than a captured list because the AddOn advances
        /// through its states while the provider lives, so a captured value would freeze at the
        /// first read. A null or throwing probe leaves the lifecycle unobservable, which M1 reports
        /// as INCONCLUSIVE.
        /// </summary>
        public Nt8HostDataProvider(Func<bool> masterMetadataReady, Func<IReadOnlyList<string>> lifecycleStates, string requestedInstrumentId = null)
        {
            _masterMetadataReady = masterMetadataReady ?? (() => false);
            _lifecycleStates = lifecycleStates;
            _requestedInstrumentId = requestedInstrumentId;
            _securityScanSource = new FileSystemSecurityScanSource();
        }

        /// <summary>
        /// True when the host has reached a state in which <c>MasterInstrument.*</c> is safe to read.
        /// A throwing readiness probe fails closed to false.
        /// </summary>
        private bool MasterMetadataReady
        {
            get
            {
                try
                {
                    return _masterMetadataReady();
                }
                catch (Exception)
                {
                    return false;
                }
            }
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
                        WorkingOrderCount = CountActiveOrders(account),
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
            if (MasterMetadataReady && instrument.MasterInstrument != null)
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

        public IReadOnlyList<string> GetMissingApis() { return new List<string>(); }

        /// <summary>
        /// Read-only production-source view for the M8 security scan. It only reads files; it never
        /// writes and never touches, mutates or reloads any NinjaTrader configuration. When the
        /// source cannot be located the scan reports UNOBSERVABLE (fail-closed), never PASS.
        /// </summary>
        public ISecurityScanSource GetSecurityScanSource()
        {
            return _securityScanSource;
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
                Lifecycle = BuildLifecycleObservation(),
                Provider = BuildProviderEvidence(),
                AccountAssociations = BuildAccountAssociations(),
                AccountIdentity = BuildAccountIdentityObservation(),
                TargetAccountConnection = BuildTargetAccountConnectionObservation(),
                OrderLifecycle = BuildOrderLifecycleObservation(),
                Attestation = BuildAttestationObservation(),
                InstrumentIdentity = BuildInstrumentIdentityObservation(),
                EsProductSpecification = BuildEsProductSpecificationObservation(),
                DomMarketData = new DomMarketDataObservation(),
                TradeDelta = new TradeDeltaObservation(),
                StrategyRiskGate = new StrategyRiskGateObservation(),
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

        /// <summary>
        /// Builds the M1 lifecycle observation from the AddOn's recorded <c>OnStateChange</c>
        /// transitions. The probe is evaluated on every read so the lifecycle reflects the host's
        /// current position, not a value captured at construction. A null or throwing probe yields
        /// an unobservable surface (M1 reports INCONCLUSIVE), never a fabricated state.
        /// </summary>
        private LifecycleObservation BuildLifecycleObservation()
        {
            if (_lifecycleStates == null)
            {
                return new LifecycleObservation { LifecycleSurfaceObservable = false };
            }

            try
            {
                var states = _lifecycleStates();
                var observation = new LifecycleObservation
                {
                    LifecycleSurfaceObservable = true
                };

                if (states != null)
                {
                    foreach (var state in states)
                    {
                        if (!string.IsNullOrEmpty(state))
                        {
                            observation.ObservedStates.Add(state);
                        }
                    }
                }

                return observation;
            }
            catch (Exception)
            {
                return new LifecycleObservation { LifecycleSurfaceObservable = false };
            }
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

            // USD is the only denomination the approved account identity accepts. Reduce the
            // observed NT8 Currency member directly: no generic ISO table and no conversion of a
            // foreign denomination into USD. An observed non-USD member stays a definite mismatch
            // (FAIL); a denomination that cannot be read stays unobservable (INCONCLUSIVE).
            var denomination = CurrencyForUsdIdentity(SafeString(() => matched.Denomination.ToString()));
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

                    // Read the account's own order collection, never a global one. A collection
                    // that could not be read is recorded as a failure rather than silently
                    // treated as empty: an unreadable collection could hide an active order, so
                    // the active-order total must stay untrustworthy (fail closed).
                    Collection<Order> orders;
                    try
                    {
                        orders = account.Orders;
                    }
                    catch (Exception)
                    {
                        observation.OrderReadFailureCount++;
                        continue;
                    }

                    if (orders == null)
                    {
                        observation.OrderReadFailureCount++;
                        continue;
                    }

                    // The collection was readable this pass, even when it is empty. An empty
                    // readable collection is a real observation ("no active orders"), not an
                    // unobservable surface.
                    observation.OrderCollectionObservable = true;

                    try
                    {
                        foreach (var order in orders)
                        {
                            if (order == null)
                            {
                                observation.OrderReadFailureCount++;
                                continue;
                            }

                            try
                            {
                                CountOrder(observation, order);
                            }
                            catch (Exception)
                            {
                                observation.OrderReadFailureCount++;
                            }
                        }
                    }
                    catch (Exception)
                    {
                        // The host collection changed while it was being enumerated (a live
                        // Account.Orders is updated off this thread). An order could have been
                        // skipped, so the active-order total must stay untrustworthy.
                        observation.OrderReadFailureCount++;
                    }
                }
            }

            return observation;
        }

        /// <summary>
        /// Reduces one read-only host order into the observation: it classifies the order id and
        /// the order's activity class (active / completed / cancelled / rejected / unclassified)
        /// from the real <c>Order.OrderState</c>, and appends a correlated lifecycle event. It
        /// never mutates the order and never guesses a state it could not read.
        /// </summary>
        private static void CountOrder(OrderLifecycleObservation observation, Order order)
        {
            observation.OrderSurfaceObservable = true;
            observation.OrderSnapshotCount++;

            var rawOrderId = SafeString(() => order.OrderId);
            var orderIdClass = ClassifyOrderId(rawOrderId);
            IncrementClass(observation.OrderIdClassCounts, orderIdClass);

            var state = MapOrderState(order.OrderState);
            CountActivity(observation, state);

            if (OrderStateClassifier.IsTerminal(state))
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

        /// <summary>
        /// Increments the activity-class bucket for an observed order state. The classification
        /// is delegated to the shared host-free <see cref="OrderStateClassifier"/> so the host
        /// adapter, the ingest consumer and the M3 probe all agree on what "active" means.
        /// </summary>
        private static void CountActivity(OrderLifecycleObservation observation, OrderLifecycleState state)
        {
            switch (OrderStateClassifier.Classify(state))
            {
                case OrderActivityClass.Active:
                    observation.ActiveOrderCount++;
                    break;
                case OrderActivityClass.Completed:
                    observation.CompletedOrderCount++;
                    break;
                case OrderActivityClass.Cancelled:
                    observation.CancelledOrderCount++;
                    break;
                case OrderActivityClass.Rejected:
                    observation.RejectedOrderCount++;
                    break;
                default:
                    observation.UnclassifiedOrderCount++;
                    break;
            }
        }

        /// <summary>
        /// Counts the account's active (still live) orders from its own read-only
        /// <c>Account.Orders</c> collection. A completed, cancelled or rejected order is never
        /// counted, so a historical order can never inflate the working-order total. An
        /// unreadable or absent collection returns zero for this account; the M3 active-order
        /// check independently records the read failure and stays fail-closed.
        /// </summary>
        private static int CountActiveOrders(Account account)
        {
            Collection<Order> orders;
            try
            {
                orders = account.Orders;
            }
            catch (Exception)
            {
                return 0;
            }

            if (orders == null)
            {
                return 0;
            }

            var active = 0;
            try
            {
                foreach (var order in orders)
                {
                    if (order == null)
                    {
                        continue;
                    }

                    OrderLifecycleState state;
                    try
                    {
                        state = MapOrderState(order.OrderState);
                    }
                    catch (Exception)
                    {
                        continue;
                    }

                    if (OrderStateClassifier.IsActive(state))
                    {
                        active++;
                    }
                }
            }
            catch (Exception)
            {
                // The collection changed while it was enumerated; the count for this account is
                // untrustworthy, so report no active orders rather than a wrong positive. The M3
                // active-order check independently records the read failure and fails closed.
                return 0;
            }

            return active;
        }

        private InstrumentAttestationObservation BuildAttestationObservation()
        {
            var instrument = ResolveAttestationInstrument();
            var observation = new InstrumentAttestationObservation
            {
                RequestedInstrumentId = instrument != null
                    ? SafeString(() => instrument.FullName) ?? string.Empty
                    : string.Empty
            };

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
                MasterMetadataAvailable = MasterMetadataReady
            };

            if (MasterMetadataReady && instrument.MasterInstrument != null)
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

        /// <summary>
        /// Resolves the instrument the attestation should target. An explicitly configured id wins;
        /// otherwise the actual loaded discovery surface (<c>Instrument.All</c>) is consulted and the
        /// instrument is used only when its root symbol matches the permanent ES root. The instrument
        /// returned is the host's own object, so the metadata that is read is the real selected
        /// instrument's, never one constructed from a configured string. Ambiguous (more than one ES
        /// root) or absent roots resolve to null so the attestation fails closed. Never throws.
        /// </summary>
        private Instrument ResolveAttestationInstrument()
        {
            return ResolveLoadedInstrument(_requestedInstrumentId, () => MasterMetadataReady);
        }

        /// <summary>
        /// Resolves the loaded ES instrument from the host discovery surface. An explicitly
        /// configured id wins; otherwise the actual loaded surface (<c>Instrument.All</c>) is
        /// consulted and the instrument is used only when its root symbol matches the permanent
        /// ES root. Ambiguous (more than one ES root) or absent roots resolve to null so the
        /// caller fails closed rather than binding to an arbitrary instrument. Never throws.
        /// </summary>
        public static Instrument ResolveLoadedInstrument(string requestedInstrumentId, Func<bool> masterMetadataReady)
        {
            try
            {
                var ready = masterMetadataReady != null && SafeBool(masterMetadataReady) == true;

                if (!string.IsNullOrEmpty(requestedInstrumentId))
                {
                    return Instrument.GetInstrument(requestedInstrumentId);
                }

                var loaded = Instrument.All;
                if (loaded == null)
                {
                    return null;
                }

                Instrument resolved = null;
                var matches = 0;
                lock (loaded)
                {
                    foreach (var candidate in loaded)
                    {
                        if (candidate == null || !ready)
                        {
                            continue;
                        }

                        var rootName = SafeString(() => candidate.MasterInstrument != null
                            ? candidate.MasterInstrument.Name
                            : string.Empty);
                        if (!ExpectedInstrumentIdentity.RootMatches(rootName))
                        {
                            continue;
                        }

                        matches++;
                        resolved = candidate;
                    }
                }

                // Ambiguity is not accepted: more than one loaded instrument carrying the ES root
                // leaves the target unresolved so the caller cannot silently pick one.
                return matches == 1 ? resolved : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Builds the privacy-preserving observation that proves the instrument NinjaTrader
        /// actually has selected/loaded is the permanent ES/CME identity. It reads the loaded
        /// instrument discovery surface (never a name constructed from a configured string) and
        /// reduces every field to booleans/counts plus a synthetic instrument reference. The
        /// exchange is only confirmed when the D-096 configured-route metadata holds; it is never
        /// inferred from the symbol text. The raw symbol never enters the observation.
        /// </summary>
        private InstrumentIdentityObservation BuildInstrumentIdentityObservation()
        {
            var observation = new InstrumentIdentityObservation();

            try
            {
                // `var` keeps the code independent of the concrete collection type the installed NT
                // build exposes for Instrument.All; only enumeration is required.
                var loaded = Instrument.All;
                if (loaded == null)
                {
                    observation.InstrumentSurfaceObservable = false;
                    return observation;
                }

                observation.InstrumentSurfaceObservable = true;

                Instrument matched = null;
                var matchingCount = 0;
                lock (loaded)
                {
                    foreach (var candidate in loaded)
                    {
                        if (candidate == null)
                        {
                            continue;
                        }

                        observation.ObservedInstrumentCount++;

                        // MasterInstrument.* (and therefore the root symbol) must only be read after
                        // DataLoaded/Realtime. Before that it is unobservable by design, so the
                        // identity stays INCONCLUSIVE rather than being reported as a wrong instrument.
                        if (!MasterMetadataReady)
                        {
                            continue;
                        }

                        // A single unreadable member must not abort the whole scan; the read failure
                        // simply leaves that field unobservable (fail closed).
                        var rootName = SafeString(() => candidate.MasterInstrument != null ? candidate.MasterInstrument.Name : string.Empty);
                        if (string.IsNullOrEmpty(rootName))
                        {
                            continue;
                        }

                        observation.RootMetadataObservable = true;

                        // Exact ordinal root comparison; a similar or differently-cased root is not a match.
                        if (ExpectedInstrumentIdentity.RootMatches(rootName))
                        {
                            matchingCount++;
                            matched = candidate;
                        }
                    }
                }

                observation.MatchingInstrumentCount = matchingCount;

                // Only a unique exact root match can be compared further; zero matches is decided by
                // the verifier (wrong or missing instrument) and no raw name is read here.
                if (matchingCount != 1 || matched == null)
                {
                    return observation;
                }

                var fullName = SafeString(() => matched.FullName);
                observation.InstrumentRef = SyntheticRef.For("instrument", fullName);

                var rootSymbol = SafeString(() => matched.MasterInstrument != null ? matched.MasterInstrument.Name : string.Empty);
                observation.RootSymbolObservable = !string.IsNullOrEmpty(rootSymbol);
                observation.RootSymbolMatches = observation.RootSymbolObservable && ExpectedInstrumentIdentity.RootMatches(rootSymbol);

                var instrumentType = SafeString(() => matched.MasterInstrument != null ? matched.MasterInstrument.InstrumentType.ToString() : string.Empty);
                observation.InstrumentTypeObservable = !string.IsNullOrEmpty(instrumentType);
                observation.InstrumentTypeMatches = observation.InstrumentTypeObservable && ExpectedInstrumentIdentity.InstrumentTypeMatches(instrumentType);

                var currency = CurrencyForUsdIdentity(SafeString(() => matched.MasterInstrument != null ? matched.MasterInstrument.Currency.ToString() : string.Empty));
                observation.CurrencyObservable = !string.IsNullOrEmpty(currency);
                observation.CurrencyMatches = observation.CurrencyObservable && ExpectedInstrumentIdentity.CurrencyMatches(currency);

                // The configured-route metadata is read first: the venue is only confirmed when the
                // selected route is Globex, the configured set is exactly {Globex} and the sole
                // connected provider path is Rithmic (D-096). It is never inferred from "ES".
                var exchangeMetadata = ReadExchangeRouteMetadata(matched);
                observation.ExchangeMetadataObservable = exchangeMetadata.Observable;
                observation.SelectedRouteIsGlobex = exchangeMetadata.SelectedGlobex;
                observation.ConfiguredExchangesAreGlobexOnly = exchangeMetadata.ConfiguredOnlyGlobex;
                observation.ProviderRouteIsRithmicOnly = exchangeMetadata.RithmicOnly;
                observation.ExchangeConfirmedCme = exchangeMetadata.Observable && exchangeMetadata.ConfirmedCme;

                // The canonical id is derived from the host symbol and the mapped exchange. It is only
                // computed when the venue was actually confirmed, so it can never be manufactured from
                // the symbol text alone.
                var canonicalId = observation.ExchangeConfirmedCme
                    ? CanonicalInstrumentId.Build(fullName, ExpectedInstrumentIdentity.Exchange)
                    : null;
                observation.SymbolParsed = CanonicalInstrumentId.TryParse(fullName, out _, out _, out _);
                observation.CanonicalInstrumentId = canonicalId ?? string.Empty;
                observation.CanonicalIdObservable = !string.IsNullOrEmpty(canonicalId);
                observation.CanonicalIdMatches = observation.CanonicalIdObservable
                    && ExpectedInstrumentIdentity.CanonicalRootVenueMatches(canonicalId);

                return observation;
            }
            catch (Exception)
            {
                observation.InstrumentSurfaceObservable = false;
                return observation;
            }
        }

        /// <summary>
        /// Builds the read-only observation that proves the loaded ES instrument's own economics
        /// match the canonical CME E-mini S&amp;P 500 Futures specification (D-095 §4). It reuses
        /// the same unique ES-root resolution as the identity observation (never a name built from
        /// a configured string), reads the selected instrument's <c>MasterInstrument.*</c> economics
        /// only after the live readiness probe returns true, and reduces them to numbers plus a
        /// synthetic reference. The raw symbol never enters the observation. Never throws.
        /// </summary>
        private EsProductSpecificationObservation BuildEsProductSpecificationObservation()
        {
            var observation = new EsProductSpecificationObservation();

            try
            {
                var instrument = ResolveLoadedInstrument(_requestedInstrumentId, () => MasterMetadataReady);
                if (instrument == null)
                {
                    // No unique ES-root instrument: distinguish "surface not enumerable" from
                    // "enumerated but no matching instrument" so the verifier can fail closed.
                    observation.InstrumentSurfaceObservable = Instrument.All != null;
                    observation.InstrumentObserved = false;
                    return observation;
                }

                observation.InstrumentSurfaceObservable = true;
                observation.InstrumentObserved = true;

                var fullName = SafeString(() => instrument.FullName);
                observation.InstrumentRef = SyntheticRef.For("instrument", fullName);

                var master = instrument.MasterInstrument;
                if (master == null)
                {
                    return observation;
                }

                var pointValue = SafeDouble(() => master.PointValue);
                observation.PointValueObservable = pointValue.HasValue;
                observation.PointValue = pointValue ?? 0d;

                var tickSize = SafeDouble(() => master.TickSize);
                observation.TickSizeObservable = tickSize.HasValue;
                observation.TickSize = tickSize ?? 0d;

                if (pointValue.HasValue && tickSize.HasValue)
                {
                    observation.TickValue = pointValue.Value * tickSize.Value;
                    observation.TickValueObservable = true;
                }

                var currency = CurrencyForUsdIdentity(SafeString(() => master.Currency.ToString()));
                observation.CurrencyObservable = !string.IsNullOrEmpty(currency);
                observation.Currency = currency ?? string.Empty;

                return observation;
            }
            catch (Exception)
            {
                observation.InstrumentSurfaceObservable = false;
                observation.InstrumentObserved = false;
                return observation;
            }
        }

        private readonly struct ExchangeRouteMetadata
        {
            public readonly bool Observable;
            public readonly bool SelectedGlobex;
            public readonly bool ConfiguredOnlyGlobex;
            public readonly bool RithmicOnly;

            public ExchangeRouteMetadata(bool observable, bool selectedGlobex, bool configuredOnlyGlobex, bool rithmicOnly)
            {
                Observable = observable;
                SelectedGlobex = selectedGlobex;
                ConfiguredOnlyGlobex = configuredOnlyGlobex;
                RithmicOnly = rithmicOnly;
            }

            public bool ConfirmedCme
            {
                get { return SelectedGlobex && ConfiguredOnlyGlobex && RithmicOnly; }
            }
        }

        /// <summary>
        /// Reads the D-096 configured-route metadata for one instrument. Every member read is
        /// guarded; a read failure marks the whole route metadata unobservable so the identity can
        /// never PASS on unconfirmed route data.
        /// </summary>
        private static ExchangeRouteMetadata ReadExchangeRouteMetadata(Instrument instrument)
        {
            try
            {
                var master = instrument.MasterInstrument;
                if (master == null || master.Exchanges == null)
                {
                    return new ExchangeRouteMetadata(false, false, false, false);
                }

                var selectedGlobex = instrument.Exchange == Exchange.Globex;
                var configuredOnlyGlobex = master.Exchanges.Count == 1 && master.Exchanges[0] == Exchange.Globex;
                var rithmicOnly = HasOnlyConnectedRithmicProvider();
                return new ExchangeRouteMetadata(true, selectedGlobex, configuredOnlyGlobex, rithmicOnly);
            }
            catch (Exception)
            {
                return new ExchangeRouteMetadata(false, false, false, false);
            }
        }

        /// <summary>
        /// Reduces an observed NT8 <c>Currency</c> member name for the USD-only identity path.
        /// <para>
        /// The approved ES/USD identity accepts exactly one currency: <c>Currency.UsDollar</c>,
        /// returned here as the ISO code the verifiers compare. Every other <em>observed</em>
        /// member is returned unchanged (its real host member name), so the verifier records a
        /// definite mismatch (FAIL) instead of dropping the observation or silently converting a
        /// foreign currency into USD. An empty/unreadable value stays empty, so an unavailable
        /// denomination or product currency is INCONCLUSIVE and can never support a PASS. There
        /// is deliberately no generic currency table in this identity path.
        /// </para>
        /// </summary>
        private static string CurrencyForUsdIdentity(string currencyMember)
        {
            if (string.IsNullOrEmpty(currencyMember))
            {
                return string.Empty;
            }

            return string.Equals(currencyMember, Currency.UsDollar.ToString(), StringComparison.Ordinal)
                ? ExpectedAccountIdentity.Currency
                : currencyMember;
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
