using System;
using System.Collections.Generic;
using RiskPilot.Diagnostics.Nt8.Core.Identity;
using RiskPilot.Diagnostics.Nt8.Core.Models.Observations;
using RiskPilot.Diagnostics.Nt8.Core.Status;

namespace RiskPilot.Diagnostics.Nt8.Core.Run
{
    /// <summary>
    /// The independent readiness facts, kept separate on purpose and each carrying its own
    /// three-valued state. The previous model collapsed several of them: it recorded
    /// <c>MetadataReady</c> from the lifecycle gate, so an AddOn that merely reached <c>Active</c>
    /// reported metadata as ready even though <c>MasterInstrument.*</c> had never been read, and a
    /// successful subscription was indistinguishable from actual market data. Each fact here is
    /// only ever what the host observed; none is derived from another without evidence.
    /// </summary>
    public struct HostReadinessFacts
    {
        /// <summary>The raw AddOn <c>State</c> member last observed, or null/empty.</summary>
        public string AddOnState;

        /// <summary>The NinjaTrader host object model was reachable.</summary>
        public ReadinessFactState HostAvailable;

        /// <summary>The lifecycle permits attempting the metadata read.</summary>
        public ReadinessFactState MetadataReadAllowed;

        /// <summary>The explicitly selected target instrument was resolved uniquely.</summary>
        public ReadinessFactState InstrumentResolved;

        /// <summary>The required metadata was actually read from the host adapter.</summary>
        public ReadinessFactState MetadataObserved;

        /// <summary>The target account's identity evidence was verified.</summary>
        public ReadinessFactState AccountVerified;

        /// <summary>The account→connection association and connection state were verified.</summary>
        public ReadinessFactState ConnectionVerified;

        /// <summary>The Level I/II market-data streams were attached.</summary>
        public ReadinessFactState MarketDataSubscribed;

        /// <summary>Actual market-data events were received.</summary>
        public ReadinessFactState MarketDataObserved;
    }

    /// <summary>
    /// Host-free derivation of the readiness facts that are expressed by other observations, so
    /// the decision stays in the core and the host layer only reads host APIs. Nothing here infers
    /// one fact from another: each fact is derived from the observation that actually proves it.
    /// </summary>
    public static class HostReadinessDerivation
    {
        /// <summary>
        /// The account-verification fact from the M2 identity observation: satisfied only when the
        /// identity verdict is PASS; unsatisfied when it is a definite FAIL; otherwise unknown (the
        /// evidence is not there yet, which is never a satisfied fact). An account merely existing
        /// in <c>Account.All</c> never makes this satisfied.
        /// </summary>
        public static ReadinessFactState AccountVerified(AccountIdentityObservation observation)
        {
            if (observation == null || !observation.AccountSurfaceObservable)
            {
                return ReadinessFactState.Unknown;
            }

            var verdict = AccountIdentityVerifier.Evaluate(observation);
            if (verdict.Status == DiagnosticStatus.PASS)
            {
                return ReadinessFactState.Satisfied;
            }

            return verdict.Status == DiagnosticStatus.FAIL
                ? ReadinessFactState.Unsatisfied
                : ReadinessFactState.Unknown;
        }

        /// <summary>
        /// The connection-verification fact: the target account's own connection association must be
        /// proven AND that connection must be observed as connected. A definite association failure
        /// or an observed disconnected connection is <see cref="ReadinessFactState.Unsatisfied"/>; a
        /// proven association on a connected connection is satisfied; anything unproven is unknown.
        /// A connected market-data connection is never mistaken for the account connection, and a
        /// disconnected connection can never keep readiness satisfied.
        /// </summary>
        public static ReadinessFactState ConnectionVerified(
            TargetAccountConnectionObservation connection,
            AccountIdentityObservation account)
        {
            if (connection == null || !connection.AccountSurfaceObservable)
            {
                return ReadinessFactState.Unknown;
            }

            var verdict = TargetAccountConnectionVerifier.Evaluate(connection);
            if (verdict.Status == DiagnosticStatus.FAIL)
            {
                return ReadinessFactState.Unsatisfied;
            }

            var statusObservable = account != null && account.ConnectionStatusObservable;
            if (statusObservable && !account.ConnectionConnected)
            {
                return ReadinessFactState.Unsatisfied;
            }

            if (verdict.Status == DiagnosticStatus.PASS && statusObservable && account.ConnectionConnected)
            {
                return ReadinessFactState.Satisfied;
            }

            return ReadinessFactState.Unknown;
        }
    }

    /// <summary>
    /// Host-free evaluation of the readiness facts into a <see cref="HostReadinessObservation"/>.
    /// The decision is fail-closed: an AddOn that has not reached its operational state, a host
    /// that is shutting down, or any missing/unobserved fact keeps the overall state below
    /// <see cref="HostReadinessState.Ready"/>. Nothing is inferred from the fact that the AddOn was
    /// constructed: readiness is only ever what the observed facts actually prove.
    /// </summary>
    public static class HostReadinessEvaluator
    {
        /// <summary>
        /// Derives the observation from the facts. A null/empty <c>AddOnState</c> means the
        /// lifecycle was not observable, which is never treated as operational.
        /// </summary>
        public static HostReadinessObservation Evaluate(HostReadinessFacts facts)
        {
            var addOnState = facts.AddOnState ?? string.Empty;
            var operational = string.IsNullOrEmpty(addOnState)
                ? ReadinessFactState.Unknown
                : ReadinessFactStates.Of(AddOnLifecycle.IsOperational(addOnState));

            var observation = new HostReadinessObservation
            {
                SurfaceObservable = true,
                AddOnState = addOnState,
                HostOperational = operational,
                ShuttingDown = AddOnLifecycle.IsShutdown(addOnState),
                HostAvailable = facts.HostAvailable,
                MetadataReadAllowed = facts.MetadataReadAllowed,
                InstrumentResolved = facts.InstrumentResolved,
                MetadataObserved = facts.MetadataObserved,
                AccountVerified = facts.AccountVerified,
                ConnectionVerified = facts.ConnectionVerified,
                MarketDataSubscribed = facts.MarketDataSubscribed,
                MarketDataObserved = facts.MarketDataObserved
            };

            // Precedence: shutdown outranks everything (readiness can never complete), then a
            // non-operational lifecycle, then each outstanding fact in dependency order. Only every
            // fact holding is READY.
            if (observation.ShuttingDown)
            {
                return Stop(observation, HostReadinessState.ShuttingDown, HostReadinessReasonCodes.ShuttingDown);
            }

            if (!observation.HostOperational.IsSatisfied())
            {
                return Stop(observation, HostReadinessState.NotOperational, HostReadinessReasonCodes.NotOperational);
            }

            if (!observation.HostAvailable.IsSatisfied())
            {
                return Stop(observation, HostReadinessState.AwaitingHost, HostReadinessReasonCodes.HostUnavailable);
            }

            if (!observation.MetadataReadAllowed.IsSatisfied())
            {
                return Stop(observation, HostReadinessState.AwaitingMetadataRead, HostReadinessReasonCodes.MetadataReadNotAllowed);
            }

            if (!observation.InstrumentResolved.IsSatisfied())
            {
                return Stop(observation, HostReadinessState.AwaitingInstrument, HostReadinessReasonCodes.InstrumentUnavailable);
            }

            if (!observation.MetadataObserved.IsSatisfied())
            {
                return Stop(observation, HostReadinessState.AwaitingMetadata, HostReadinessReasonCodes.MetadataUnavailable);
            }

            if (!observation.AccountVerified.IsSatisfied())
            {
                return Stop(observation, HostReadinessState.AwaitingAccount, HostReadinessReasonCodes.AccountUnverified);
            }

            if (!observation.ConnectionVerified.IsSatisfied())
            {
                return Stop(observation, HostReadinessState.AwaitingConnection, HostReadinessReasonCodes.ConnectionUnverified);
            }

            if (!observation.MarketDataSubscribed.IsSatisfied())
            {
                return Stop(observation, HostReadinessState.AwaitingSubscription, HostReadinessReasonCodes.NotSubscribed);
            }

            if (!observation.MarketDataObserved.IsSatisfied())
            {
                return Stop(observation, HostReadinessState.AwaitingMarketData, HostReadinessReasonCodes.MarketDataNotObserved);
            }

            observation.State = HostReadinessState.Ready;
            observation.FullyReady = true;
            observation.ReasonCodes.Add(HostReadinessReasonCodes.Ready);
            return observation;
        }

        private static HostReadinessObservation Stop(HostReadinessObservation observation, string state, string reasonCode)
        {
            observation.State = state;
            observation.FullyReady = false;
            observation.ReasonCodes.Add(reasonCode);
            return observation;
        }

        /// <summary>
        /// A not-observed readiness surface. Used when no tracker supplied facts; the observation
        /// never reports ready.
        /// </summary>
        public static HostReadinessObservation NotObserved()
        {
            var observation = new HostReadinessObservation { SurfaceObservable = false };
            observation.ReasonCodes.Add(HostReadinessReasonCodes.Unobservable);
            return observation;
        }
    }

    /// <summary>
    /// Thread-safe, host-free holder of the current readiness facts. The host layer records each
    /// fact as it becomes observable (from the AddOn lifecycle callback, the instrument resolution,
    /// the metadata read, the account/connection verification and the market-data subscription and
    /// events) and the provider reads the derived observation on demand. A single private lock
    /// guards the facts so a host callback thread and the diagnostic worker thread can update and
    /// read concurrently without a torn observation.
    /// <para>
    /// The tracker owns a <em>lifecycle epoch</em>. <see cref="BeginEpoch"/> clears every previously
    /// observed fact, so a fact observed in an earlier lifecycle epoch can never keep a restarted
    /// AddOn reported as ready. <see cref="InvalidateStreamFacts"/> clears the connection- and
    /// stream-dependent facts after a connection loss/reconnect, so readiness cannot remain
    /// satisfied on evidence that no longer holds.
    /// </para>
    /// </summary>
    public sealed class HostReadinessTracker
    {
        private readonly object _gate = new object();
        private string _addOnState = string.Empty;
        private long _epoch;
        private ReadinessFactState _hostAvailable;
        private ReadinessFactState _metadataReadAllowed;
        private ReadinessFactState _instrumentResolved;
        private ReadinessFactState _metadataObserved;
        private ReadinessFactState _accountVerified;
        private ReadinessFactState _connectionVerified;
        private ReadinessFactState _marketDataSubscribed;
        private ReadinessFactState _marketDataObserved;

        /// <summary>
        /// Begins a new lifecycle epoch: every previously observed fact is cleared, so a fact from
        /// an earlier epoch can never keep a restarted AddOn reported as ready. The raw AddOn state
        /// is left to be recorded by <see cref="RecordAddOnState"/>.
        /// </summary>
        public void BeginEpoch()
        {
            lock (_gate)
            {
                _epoch++;
                _hostAvailable = ReadinessFactState.Unknown;
                _metadataReadAllowed = ReadinessFactState.Unknown;
                _instrumentResolved = ReadinessFactState.Unknown;
                _metadataObserved = ReadinessFactState.Unknown;
                _accountVerified = ReadinessFactState.Unknown;
                _connectionVerified = ReadinessFactState.Unknown;
                _marketDataSubscribed = ReadinessFactState.Unknown;
                _marketDataObserved = ReadinessFactState.Unknown;
            }
        }

        /// <summary>
        /// Clears the connection- and stream-dependent facts after a connection loss/reconnect. The
        /// lifecycle/metadata/instrument facts are unaffected, because a reconnect does not change
        /// them. Readiness cannot remain satisfied on a subscription or a connection that no longer
        /// holds.
        /// </summary>
        public void InvalidateStreamFacts()
        {
            lock (_gate)
            {
                _connectionVerified = ReadinessFactState.Unknown;
                _marketDataSubscribed = ReadinessFactState.Unknown;
                _marketDataObserved = ReadinessFactState.Unknown;
            }
        }

        /// <summary>Records the raw AddOn <c>State</c> member just observed.</summary>
        public void RecordAddOnState(string state)
        {
            lock (_gate)
            {
                _addOnState = state ?? string.Empty;
            }
        }

        /// <summary>Records whether the host object model was reachable.</summary>
        public void RecordHostAvailable(ReadinessFactState state)
        {
            lock (_gate) { _hostAvailable = state; }
        }

        /// <summary>Records whether the lifecycle permits attempting the metadata read.</summary>
        public void RecordMetadataReadAllowed(ReadinessFactState state)
        {
            lock (_gate) { _metadataReadAllowed = state; }
        }

        /// <summary>Records whether the target instrument was resolved uniquely.</summary>
        public void RecordInstrumentResolved(ReadinessFactState state)
        {
            lock (_gate) { _instrumentResolved = state; }
        }

        /// <summary>Records whether the required metadata was actually read.</summary>
        public void RecordMetadataObserved(ReadinessFactState state)
        {
            lock (_gate) { _metadataObserved = state; }
        }

        /// <summary>Records the target-account identity verification fact.</summary>
        public void RecordAccountVerified(ReadinessFactState state)
        {
            lock (_gate) { _accountVerified = state; }
        }

        /// <summary>Records the account→connection association verification fact.</summary>
        public void RecordConnectionVerified(ReadinessFactState state)
        {
            lock (_gate) { _connectionVerified = state; }
        }

        /// <summary>Records whether the Level I/II streams were attached.</summary>
        public void RecordMarketDataSubscribed(ReadinessFactState state)
        {
            lock (_gate) { _marketDataSubscribed = state; }
        }

        /// <summary>Records whether actual market-data events were received.</summary>
        public void RecordMarketDataObserved(ReadinessFactState state)
        {
            lock (_gate) { _marketDataObserved = state; }
        }

        /// <summary>The raw AddOn state last recorded.</summary>
        public string AddOnState { get { lock (_gate) { return _addOnState; } } }

        /// <summary>The current lifecycle epoch.</summary>
        public long Epoch { get { lock (_gate) { return _epoch; } } }

        /// <summary>Derives the readiness observation from the current facts.</summary>
        public HostReadinessObservation Observe()
        {
            HostReadinessFacts facts;
            long epoch;
            lock (_gate)
            {
                facts = new HostReadinessFacts
                {
                    AddOnState = _addOnState,
                    HostAvailable = _hostAvailable,
                    MetadataReadAllowed = _metadataReadAllowed,
                    InstrumentResolved = _instrumentResolved,
                    MetadataObserved = _metadataObserved,
                    AccountVerified = _accountVerified,
                    ConnectionVerified = _connectionVerified,
                    MarketDataSubscribed = _marketDataSubscribed,
                    MarketDataObserved = _marketDataObserved
                };
                epoch = _epoch;
            }

            var observation = HostReadinessEvaluator.Evaluate(facts);
            observation.Epoch = epoch;
            return observation;
        }
    }

    /// <summary>
    /// The retry/backoff policy for the readiness polling loop. It is host-free and deterministic:
    /// the loop supplies the current attempt count and the clock, so a frozen/deterministic clock
    /// can never make the loop wait unbounded or spin. Backoff grows geometrically from
    /// <see cref="InitialDelayMs"/> to <see cref="MaxDelayMs"/> and the loop stops after
    /// <see cref="MaxAttempts"/> (returning false) so it always terminates.
    /// </summary>
    public sealed class HostReadinessRetryPolicy
    {
        public HostReadinessRetryPolicy(int maxAttempts, int initialDelayMs, int maxDelayMs)
        {
            MaxAttempts = maxAttempts > 0 ? maxAttempts : 1;
            InitialDelayMs = initialDelayMs > 0 ? initialDelayMs : 1;
            MaxDelayMs = maxDelayMs >= InitialDelayMs ? maxDelayMs : InitialDelayMs;
        }

        /// <summary>A default policy: 60 attempts, 250 ms growing to 4 s (roughly two minutes).</summary>
        public static HostReadinessRetryPolicy Default()
        {
            return new HostReadinessRetryPolicy(60, 250, 4000);
        }

        public int MaxAttempts { get; private set; }

        public int InitialDelayMs { get; private set; }

        public int MaxDelayMs { get; private set; }

        /// <summary>True when another attempt is allowed after <paramref name="attemptsSoFar"/> attempts.</summary>
        public bool ShouldRetry(int attemptsSoFar)
        {
            return attemptsSoFar < MaxAttempts;
        }

        /// <summary>
        /// The delay before the next attempt, given the number of attempts already made (0-based).
        /// The delay doubles each attempt up to <see cref="MaxDelayMs"/>; it never overflows and
        /// never returns a negative value.
        /// </summary>
        public int NextDelayMs(int attemptsSoFar)
        {
            var delay = (long)InitialDelayMs;
            for (var i = 0; i < attemptsSoFar && delay < MaxDelayMs; i++)
            {
                delay *= 2;
            }

            return delay >= MaxDelayMs ? MaxDelayMs : (int)delay;
        }

        /// <summary>
        /// True when the deadline has been reached. Used together with <see cref="ShouldRetry"/> so
        /// the loop stops on whichever bound is hit first.
        /// </summary>
        public bool DeadlineReached(DateTime startedUtc, DateTime nowUtc, int totalBudgetMs)
        {
            return (nowUtc - startedUtc).TotalMilliseconds >= totalBudgetMs;
        }
    }
}
