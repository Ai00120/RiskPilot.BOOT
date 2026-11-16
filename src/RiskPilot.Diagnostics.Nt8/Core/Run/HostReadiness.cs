using System;
using System.Collections.Generic;
using RiskPilot.Diagnostics.Nt8.Core.Identity;
using RiskPilot.Diagnostics.Nt8.Core.Models.Observations;

namespace RiskPilot.Diagnostics.Nt8.Core.Run
{
    /// <summary>
    /// The five independent readiness facts, kept separate on purpose. The previous single
    /// <c>HostIsLive</c> Boolean collapsed all of them into one value keyed on data-processing
    /// states an AddOn never reaches, so it was permanently false. Separating them lets the tool
    /// wait precisely for the outstanding fact and report exactly which one is missing.
    /// </summary>
    public struct HostReadinessFacts
    {
        /// <summary>The raw AddOn <c>State</c> member last observed, or null/empty.</summary>
        public string AddOnState;

        /// <summary>True when the NinjaTrader host object model was reachable.</summary>
        public bool HostAvailable;

        /// <summary>True when the resolved instrument's master metadata was readable.</summary>
        public bool MetadataReady;

        /// <summary>True when a target instrument was resolved.</summary>
        public bool InstrumentResolved;

        /// <summary>True when the Level I/II market-data streams were attached.</summary>
        public bool MarketDataSubscribed;
    }

    /// <summary>
    /// Host-free evaluation of the readiness facts into a <see cref="HostReadinessObservation"/>.
    /// The decision is fail-closed: an AddOn that has not reached its operational state, a host
    /// that is shutting down, or any missing fact keeps the overall state below
    /// <see cref="HostReadinessState.Ready"/>. Nothing is inferred from the fact that the AddOn was
    /// constructed: readiness is only ever what the observed facts actually prove.
    /// </summary>
    public static class HostReadinessEvaluator
    {
        /// <summary>
        /// Derives the observation from the five facts. A null/empty <c>AddOnState</c> means the
        /// lifecycle was not observable, which is never treated as operational.
        /// </summary>
        public static HostReadinessObservation Evaluate(HostReadinessFacts facts)
        {
            var observation = new HostReadinessObservation
            {
                SurfaceObservable = true,
                AddOnState = facts.AddOnState ?? string.Empty,
                LifecycleOperational = AddOnLifecycle.IsOperational(facts.AddOnState),
                ShuttingDown = AddOnLifecycle.IsShutdown(facts.AddOnState),
                HostAvailable = facts.HostAvailable,
                MetadataReady = facts.MetadataReady,
                InstrumentReady = facts.InstrumentResolved,
                MarketDataSubscribed = facts.MarketDataSubscribed
            };

            // Precedence: shutdown outranks everything (readiness can never complete), then a
            // non-operational lifecycle (the AddOn has not reached Active), then each outstanding
            // fact in dependency order. Only all-five-hold is READY.
            if (observation.ShuttingDown)
            {
                observation.State = HostReadinessState.ShuttingDown;
                observation.ReasonCodes.Add(HostReadinessReasonCodes.ShuttingDown);
                return observation;
            }

            if (!observation.LifecycleOperational)
            {
                observation.State = HostReadinessState.NotOperational;
                observation.ReasonCodes.Add(HostReadinessReasonCodes.NotOperational);
                return observation;
            }

            if (!observation.HostAvailable)
            {
                observation.State = HostReadinessState.AwaitingHost;
                observation.ReasonCodes.Add(HostReadinessReasonCodes.HostUnavailable);
                return observation;
            }

            if (!observation.InstrumentReady)
            {
                observation.State = HostReadinessState.AwaitingInstrument;
                observation.ReasonCodes.Add(HostReadinessReasonCodes.InstrumentUnavailable);
                return observation;
            }

            if (!observation.MetadataReady)
            {
                observation.State = HostReadinessState.AwaitingMetadata;
                observation.ReasonCodes.Add(HostReadinessReasonCodes.MetadataUnavailable);
                return observation;
            }

            if (!observation.MarketDataSubscribed)
            {
                observation.State = HostReadinessState.AwaitingSubscription;
                observation.ReasonCodes.Add(HostReadinessReasonCodes.NotSubscribed);
                return observation;
            }

            observation.State = HostReadinessState.Ready;
            observation.FullyReady = true;
            observation.ReasonCodes.Add(HostReadinessReasonCodes.Ready);
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
    /// fact as it becomes observable (from the AddOn lifecycle callback, the instrument resolution
    /// and the market-data subscription) and the provider reads the derived observation on demand.
    /// A single private lock guards the facts so a host callback thread and the diagnostic worker
    /// thread can update and read concurrently without a torn observation.
    /// </summary>
    public sealed class HostReadinessTracker
    {
        private readonly object _gate = new object();
        private string _addOnState = string.Empty;
        private bool _hostAvailable;
        private bool _metadataReady;
        private bool _instrumentResolved;
        private bool _marketDataSubscribed;

        /// <summary>Records the raw AddOn <c>State</c> member just observed.</summary>
        public void RecordAddOnState(string state)
        {
            lock (_gate)
            {
                _addOnState = state ?? string.Empty;
            }
        }

        /// <summary>Records that the host object model was reachable.</summary>
        public void RecordHostAvailable(bool available)
        {
            lock (_gate)
            {
                _hostAvailable = available;
            }
        }

        /// <summary>Records whether the resolved instrument's master metadata was readable.</summary>
        public void RecordMetadataReady(bool ready)
        {
            lock (_gate)
            {
                _metadataReady = ready;
            }
        }

        /// <summary>Records whether a target instrument was resolved.</summary>
        public void RecordInstrumentResolved(bool resolved)
        {
            lock (_gate)
            {
                _instrumentResolved = resolved;
            }
        }

        /// <summary>Records whether the Level I/II market-data streams were attached.</summary>
        public void RecordMarketDataSubscribed(bool subscribed)
        {
            lock (_gate)
            {
                _marketDataSubscribed = subscribed;
            }
        }

        /// <summary>The raw AddOn state last recorded.</summary>
        public string AddOnState { get { lock (_gate) { return _addOnState; } } }

        /// <summary>Derives the readiness observation from the current facts.</summary>
        public HostReadinessObservation Observe()
        {
            HostReadinessFacts facts;
            lock (_gate)
            {
                facts = new HostReadinessFacts
                {
                    AddOnState = _addOnState,
                    HostAvailable = _hostAvailable,
                    MetadataReady = _metadataReady,
                    InstrumentResolved = _instrumentResolved,
                    MarketDataSubscribed = _marketDataSubscribed
                };
            }

            return HostReadinessEvaluator.Evaluate(facts);
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
