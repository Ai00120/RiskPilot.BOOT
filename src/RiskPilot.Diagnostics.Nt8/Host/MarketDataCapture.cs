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
        private readonly string _requestedInstrumentId;

        private readonly object _gate = new object();
        private Instrument _instrument;
        private bool _subscribed;

        public MarketDataCapture(Func<NormalizedRecord, bool> ingest, Func<bool> masterMetadataReady, string requestedInstrumentId = null)
        {
            _ingest = ingest;
            _masterMetadataReady = masterMetadataReady ?? (() => false);
            _requestedInstrumentId = requestedInstrumentId;
        }

        /// <summary>True once the capture subscribed to a resolved instrument's data streams.</summary>
        public bool IsSubscribed { get { lock (_gate) { return _subscribed; } } }

        /// <summary>
        /// Resolves the loaded ES instrument and subscribes to its Level I and Level II streams.
        /// Returns false (without throwing) when no single ES instrument is loaded yet, so the
        /// caller can retry once the host reaches a data state. Idempotent: a second call after a
        /// successful subscription is a no-op.
        /// </summary>
        public bool TrySubscribe()
        {
            lock (_gate)
            {
                if (_subscribed && _instrument != null)
                {
                    return true;
                }

                try
                {
                    var instrument = Nt8HostDataProvider.ResolveLoadedInstrument(_requestedInstrumentId, _masterMetadataReady);
                    if (instrument == null)
                    {
                        return false;
                    }

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
                    return true;
                }
                catch (Exception)
                {
                    // A host read failure is not fatal: the capture simply stays unsubscribed and
                    // M5 reports the market-data surface as unobserved (INCONCLUSIVE).
                    return false;
                }
            }
        }

        /// <summary>Detaches from the host events. Safe to call when never subscribed.</summary>
        public void Detach()
        {
            Instrument instrument;
            bool subscribed;
            lock (_gate)
            {
                instrument = _instrument;
                subscribed = _subscribed;
                _instrument = null;
                _subscribed = false;
            }

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
