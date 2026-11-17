using System;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using RiskPilot.Diagnostics.Nt8.Core.Models;

namespace RiskPilot.Diagnostics.Nt8.Host
{
    /// <summary>
    /// Read-only capture adapter for the NT8 Level I (<c>Instrument.MarketData</c>) and Level II
    /// (<c>Instrument.MarketDepth</c>) streams. It subscribes to the public events of the ES
    /// instrument NinjaTrader actually has loaded and converts each event into a normalized
    /// <see cref="MarketDataRecord"/> / <see cref="DomRecord"/> that is handed to the ingest
    /// pipeline. This is what makes M5 report real host data instead of an always-empty
    /// observation.
    /// <para>
    /// It is strictly read-only: it subscribes to events, never submits/changes/cancels an order,
    /// and never mutates an account, connection, position or setting. Subscribing on the
    /// instrument's own dispatcher thread (async post only) keeps a host callback from blocking.
    /// </para>
    /// </summary>
    public sealed class MarketDataCapture
    {
        private readonly Func<NormalizedRecord, bool> _ingest;
        private readonly Func<bool> _masterMetadataReady;
        private readonly Func<string> _requestedInstrumentIdProbe;
        private readonly Core.Run.HostReadinessTracker _readiness;

        private readonly object _gate = new object();
        private Instrument _instrument;
        private bool _subscribed;

        public MarketDataCapture(Func<NormalizedRecord, bool> ingest, Func<bool> masterMetadataReady, Func<string> requestedInstrumentIdProbe = null)
            : this(ingest, masterMetadataReady, requestedInstrumentIdProbe, null)
        {
        }

        /// <summary>
        /// Creates the capture with the optional readiness tracker. The tracker is updated as each
        /// readiness fact becomes true (instrument resolved, master metadata readable, streams
        /// attached), so the host can report exactly which fact is still outstanding instead of a
        /// single conflated Boolean.
        /// <para>
        /// <paramref name="requestedInstrumentIdProbe"/> is a live probe: the operator selects the
        /// target instrument in the Diagnostic Center after the capture is constructed, so the
        /// current selection must be read on each subscription attempt, not captured.
        /// </para>
        /// </summary>
        public MarketDataCapture(
            Func<NormalizedRecord, bool> ingest,
            Func<bool> masterMetadataReady,
            Func<string> requestedInstrumentIdProbe,
            Core.Run.HostReadinessTracker readiness)
        {
            _ingest = ingest;
            _masterMetadataReady = masterMetadataReady ?? (() => false);
            _requestedInstrumentIdProbe = requestedInstrumentIdProbe;
            _readiness = readiness;
        }

        /// <summary>
        /// The operator's current explicit target instrument, evaluated live. A null or throwing
        /// probe is "no selection" (fail closed), never an implicit instrument.
        /// </summary>
        private string RequestedInstrumentId
        {
            get
            {
                try
                {
                    return _requestedInstrumentIdProbe != null ? _requestedInstrumentIdProbe() : null;
                }
                catch (Exception)
                {
                    return null;
                }
            }
        }

        /// <summary>True once the capture subscribed to a resolved instrument's data streams.</summary>
        public bool IsSubscribed { get { lock (_gate) { return _subscribed; } } }

        /// <summary>
        /// Resolves the explicitly selected instrument and subscribes to its Level I and Level II
        /// streams. Returns false (without throwing) when no single instrument is selected/loaded
        /// yet, so the caller can retry once the host reaches a data state.
        /// <para>
        /// Idempotent for the same target: a repeated call while already subscribed to the resolved
        /// instrument is a no-op, so the readiness retry loop can never create a duplicate
        /// subscription. When the operator changes the selection to a different loaded contract,
        /// the capture detaches from the previous instrument before subscribing to the new one, so
        /// a re-target never leaves two live subscriptions behind.
        /// </para>
        /// </summary>
        public bool TrySubscribe()
        {
            lock (_gate)
            {
                try
                {
                    // The master-metadata readiness gate decides whether MasterInstrument.* is safe to
                    // read; it is true from the AddOn's operational (Active) state onward. Recording it
                    // here keeps the readiness observation honest: metadata is only ready once the host
                    // actually reports it is readable.
                    var metadataReady = false;
                    try
                    {
                        metadataReady = _masterMetadataReady();
                    }
                    catch (Exception)
                    {
                        metadataReady = false;
                    }

                    if (_readiness != null)
                    {
                        _readiness.RecordMetadataReady(metadataReady);
                    }

                    var instrument = Nt8HostDataProvider.ResolveLoadedInstrument(RequestedInstrumentId, _masterMetadataReady);
                    if (instrument == null)
                    {
                        // The explicit selection no longer resolves (removed, ambiguous or unset). Detach
                        // any previous subscription so a stale stream is not kept, and report the
                        // instrument fact as unresolved (fail closed) rather than keeping a stale one.
                        DetachInstrumentLocked();
                        if (_readiness != null)
                        {
                            _readiness.RecordInstrumentResolved(false);
                        }

                        return false;
                    }

                    if (_readiness != null)
                    {
                        _readiness.RecordInstrumentResolved(true);
                    }

                    // Already subscribed to this exact instrument: nothing to do.
                    if (_subscribed && ReferenceEquals(_instrument, instrument))
                    {
                        return true;
                    }

                    // The selection changed (or this is the first subscription): drop any previous
                    // subscription first so two contracts are never subscribed at once.
                    DetachInstrumentLocked();

                    var marketData = instrument.MarketData;
                    var marketDepth = instrument.MarketDepth;
                    if (marketData == null || marketDepth == null)
                    {
                        return false;
                    }

                    // Subscribe on the instrument's own dispatcher thread (async post only), which
                    // is the documented pattern for instrument event subscription. A dispatcher
                    // that has already begun shutting down cannot accept a post, so the capture
                    // stays unsubscribed rather than throwing into the host callback.
                    var dispatcher = instrument.Dispatcher;
                    if (dispatcher != null)
                    {
                        if (dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
                        {
                            return false;
                        }

                        dispatcher.InvokeAsync(() =>
                        {
                            marketData.Update += OnMarketData;
                            marketDepth.Update += OnMarketDepth;
                        });
                    }
                    else
                    {
                        marketData.Update += OnMarketData;
                        marketDepth.Update += OnMarketDepth;
                    }

                    _instrument = instrument;
                    _subscribed = true;
                    if (_readiness != null)
                    {
                        _readiness.RecordMarketDataSubscribed(true);
                    }

                    return true;
                }
                catch (Exception)
                {
                    // A host read failure is not fatal: the capture simply stays unsubscribed and
                    // M5 reports the market-data surface as unobserved (INCONCLUSIVE).
                    if (_readiness != null)
                    {
                        _readiness.RecordMarketDataSubscribed(false);
                    }

                    return false;
                }
            }
        }

        /// <summary>Detaches from the host events. Safe to call when never subscribed.</summary>
        public void Detach()
        {
            lock (_gate)
            {
                DetachInstrumentLocked();
            }

            if (_readiness != null)
            {
                _readiness.RecordMarketDataSubscribed(false);
            }
        }

        /// <summary>
        /// Clears the tracked instrument and detaches its host handlers. Caller must hold
        /// <see cref="_gate"/>. Safe when never subscribed. Never throws.
        /// </summary>
        private void DetachInstrumentLocked()
        {
            var instrument = _instrument;
            var subscribed = _subscribed;
            _instrument = null;
            _subscribed = false;

            if (!subscribed || instrument == null)
            {
                return;
            }

            try
            {
                var marketData = instrument.MarketData;
                var marketDepth = instrument.MarketDepth;
                var dispatcher = instrument.Dispatcher;
                if (dispatcher != null)
                {
                    dispatcher.InvokeAsync(() =>
                    {
                        if (marketData != null) marketData.Update -= OnMarketData;
                        if (marketDepth != null) marketDepth.Update -= OnMarketDepth;
                    });
                }
                else
                {
                    if (marketData != null) marketData.Update -= OnMarketData;
                    if (marketDepth != null) marketDepth.Update -= OnMarketDepth;
                }
            }
            catch (Exception)
            {
                // Detach never throws; a failed detach leaves the process to exit with the host.
            }
        }

        private void OnMarketData(object sender, MarketDataEventArgs args)
        {
            if (args == null)
            {
                return;
            }

            try
            {
                // The Level I Last event carries no aggressor side. Record the best bid/ask the host
                // reported on this same event (0 = not populated) so the M6 consumer can derive the
                // direction from real host data instead of counting every print as unknown. No
                // direction is asserted here: the derivation and its provenance live in the core.
                var record = new MarketDataRecord
                {
                    InstrumentId = args.Instrument != null ? args.Instrument.FullName : string.Empty,
                    Kind = MapMarketDataKind(args.MarketDataType),
                    Bid = args.Bid,
                    Ask = args.Ask,
                    Last = args.Price,
                    Volume = args.Volume,
                    IsReset = args.IsReset,
                    SourceTimestampUtc = NormalizeUtc(args.Time),
                    SourceTimeBasis = SourceTimeBasis.ServerTimeUnverified
                };

                _ingest?.Invoke(record);
            }
            catch (Exception)
            {
                // A conversion failure must never disrupt the host callback thread; the event is
                // simply not ingested and the loss is visible through the ingest counters.
            }
        }

        private void OnMarketDepth(object sender, MarketDepthEventArgs args)
        {
            if (args == null)
            {
                return;
            }

            try
            {
                var record = new DomRecord
                {
                    InstrumentId = args.Instrument != null ? args.Instrument.FullName : string.Empty,
                    Operation = MapDomOperation(args.Operation),
                    Position = args.Position,
                    Price = args.Price,
                    Volume = args.Volume,
                    IsReset = args.IsReset,
                    SourceTimestampUtc = NormalizeUtc(args.Time),
                    SourceTimeBasis = SourceTimeBasis.ServerTimeUnverified
                };

                _ingest?.Invoke(record);
            }
            catch (Exception)
            {
                // See OnMarketData: never disrupt the host callback thread.
            }
        }

        /// <summary>Maps the public Level I market-data type onto the normalized kind.</summary>
        public static MarketDataKind MapMarketDataKind(MarketDataType type)
        {
            switch (type)
            {
                case MarketDataType.Ask:
                case MarketDataType.Bid:
                    return MarketDataKind.Quote;
                case MarketDataType.Last:
                    return MarketDataKind.Trade;
                default:
                    // Daily/settlement/opening/open-interest snapshots are not Level I
                    // quote/trade evidence and must not be reported as such.
                    return MarketDataKind.Unknown;
            }
        }

        /// <summary>Maps the public depth operation onto the normalized DOM operation.</summary>
        public static DomOperation MapDomOperation(Operation operation)
        {
            switch (operation)
            {
                case Operation.Add:
                    return DomOperation.Insert;
                case Operation.Update:
                    return DomOperation.Update;
                case Operation.Remove:
                    return DomOperation.Remove;
                default:
                    return DomOperation.Unknown;
            }
        }

        private static DateTime? NormalizeUtc(DateTime value)
        {
            if (value == default(DateTime))
            {
                return null;
            }

            return value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
        }
    }
}
