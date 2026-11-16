// ---------------------------------------------------------------------------
// NinjaTraderStubs.cs — compile-time stubs for OFFLINE syntax validation only.
// ---------------------------------------------------------------------------
// These stubs let the project build on machines without NinjaTrader installed
// (e.g. Linux CI runners) using -p:UseStubs=true. THEY ARE NOT PART OF THE
// RUNTIME ADDON. When compiled against the real host (UseStubs=false), the real
// NinjaTrader assemblies are referenced instead and this file is excluded.
//
// Signatures are derived from the public NinjaScript documentation and from the
// API surface confirmed in Ai00120/RiskPilot.BOT. If a signature drifts from the
// installed NT build, the real build fails — which is the correct, fail-closed
// outcome. A successful stub build is NOT proof of real host compatibility.
// ---------------------------------------------------------------------------

#if USE_STUBS

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace NinjaTrader.NinjaScript
{
    public enum State
    {
        SetDefaults,
        Configure,
        Active,
        DataLoaded,
        Historical,
        Transition,
        Realtime,
        Terminated
    }

    public enum PrintTo
    {
        OutputTab1,
        OutputTab2
    }

    public abstract class AddOnBase
    {
        protected State State { get; set; }
        protected string Name { get; set; }
        protected string Description { get; set; }

        protected virtual void OnStateChange() { }
        protected virtual void OnWindowCreated(System.Windows.Window window) { }
        protected virtual void OnWindowDestroyed(System.Windows.Window window) { }

        protected void Log(string message, NinjaTrader.Cbi.LogLevel logLevel) { }
        protected void Print(string message) { }
    }
}

namespace NinjaTrader.Cbi
{
    public enum LogLevel
    {
        Information,
        Warning,
        Error,
        Alert
    }

    public enum ConnectionStatus
    {
        Connected,
        Connecting,
        ConnectionLost,
        Disconnecting,
        Disconnected
    }

    public enum Mode
    {
        Live,
        Simulation
    }

    // Mirrors the real NT8 8.x NinjaTrader.Cbi.Provider enum (reflection-verified):
    // named members Collective2=0..Yahoo=19, then generic placeholder slots
    // Provider1..Provider40 = 20..59, Custom0..Custom45 = 1000..1045 and Unknown=9999.
    // There is deliberately NO 'Rithmic' member: the real enum has none, and an adapter
    // without a dedicated member is assigned a generic ProviderN/CustomN slot (the host
    // reports it as e.g. 'Provider14'). See Core/Identity/ProviderIdentity.cs.
    public enum Provider
    {
        Collective2 = 0,
        CyberTrader = 1,
        Dtn = 2,
        ESignal = 3,
        External = 4,
        FFastFill = 5,
        Gain = 6,
        Hosted = 7,
        InteractiveBrokers = 8,
        MBTrading = 9,
        OpenTick = 10,
        Patsystems = 11,
        Photon = 12,
        Playback = 13,
        RolfeAndNolan = 14,
        Simulator = 15,
        Tda = 16,
        TrackData = 17,
        TradingTechnologies = 18,
        Yahoo = 19,
        Provider1 = 20,
        Provider2 = 21,
        Provider3 = 22,
        Provider4 = 23,
        Provider5 = 24,
        Provider6 = 25,
        Provider7 = 26,
        Provider8 = 27,
        Provider9 = 28,
        Provider10 = 29,
        Provider11 = 30,
        Provider12 = 31,
        Provider13 = 32,
        Provider14 = 33,
        Provider15 = 34,
        Provider16 = 35,
        Provider17 = 36,
        Provider18 = 37,
        Provider19 = 38,
        Provider20 = 39,
        Provider21 = 40,
        Provider22 = 41,
        Provider23 = 42,
        Provider24 = 43,
        Provider25 = 44,
        Provider26 = 45,
        Provider27 = 46,
        Provider28 = 47,
        Provider29 = 48,
        Provider30 = 49,
        Provider31 = 50,
        Provider32 = 51,
        Provider33 = 52,
        Provider34 = 53,
        Provider35 = 54,
        Provider36 = 55,
        Provider37 = 56,
        Provider38 = 57,
        Provider39 = 58,
        Provider40 = 59,
        Custom0 = 1000,
        Custom1 = 1001,
        Custom2 = 1002,
        Custom3 = 1003,
        Custom4 = 1004,
        Custom5 = 1005,
        Custom6 = 1006,
        Custom7 = 1007,
        Custom8 = 1008,
        Custom9 = 1009,
        Custom10 = 1010,
        Custom11 = 1011,
        Custom12 = 1012,
        Custom13 = 1013,
        Custom14 = 1014,
        Custom15 = 1015,
        Custom16 = 1016,
        Custom17 = 1017,
        Custom18 = 1018,
        Custom19 = 1019,
        Custom20 = 1020,
        Custom21 = 1021,
        Custom22 = 1022,
        Custom23 = 1023,
        Custom24 = 1024,
        Custom25 = 1025,
        Custom26 = 1026,
        Custom27 = 1027,
        Custom28 = 1028,
        Custom29 = 1029,
        Custom30 = 1030,
        Custom31 = 1031,
        Custom32 = 1032,
        Custom33 = 1033,
        Custom34 = 1034,
        Custom35 = 1035,
        Custom36 = 1036,
        Custom37 = 1037,
        Custom38 = 1038,
        Custom39 = 1039,
        Custom40 = 1040,
        Custom41 = 1041,
        Custom42 = 1042,
        Custom43 = 1043,
        Custom44 = 1044,
        Custom45 = 1045,
        Unknown = 9999
    }

    /// <summary>
    /// Models NT8 <c>NinjaTrader.Cbi.Currency</c> names and values from a public reflected
    /// API snapshot (not verified against 8.1.9.0 here; see docs/NT8_COMPILATION_FIXES.md). There is NO
    /// <c>Yen</c> member: the JPY member is <c>JapaneseYen</c>, and the GBP member is
    /// <c>BritishPound</c> (not <c>BritishPounds</c>). Only <c>UsDollar</c> is used by the
    /// approved ES/USD identity path; the rest exist so an offline stub build cannot silently
    /// diverge from the real API surface.
    /// </summary>
    public enum Currency
    {
        AustralianDollar = 0,
        BritishPound = 1,
        CanadianDollar = 2,
        Euro = 3,
        HongKongDollar = 4,
        JapaneseYen = 5,
        SwissFranc = 6,
        UsDollar = 7,
        KoreanWon = 8,
        IndianRupee = 9,
        SwedishKrona = 10,
        MexicanPeso = 11,
        BrazilianReal = 12,
        SingaporeDollar = 13,
        ChinaYuan = 14,
        MalaysiaRinggit = 15,
        ThailandBaht = 16,
        TaiwanNewDollar = 17,
        NewZealandDollar = 18,
        SouthAfricanRand = 19,
        CzechRepublicKoruna = 20,
        NorwayKrone = 21,
        DenmarkKrone = 22,
        HungaryForint = 23,
        PolandZloty = 24,
        TurkeyLira = 25,
        IsraeliShekel = 26,
        RussiaRuble = 27,
        SaudiRiyal = 28
    }

    public enum Exchange
    {
        Default,
        Globex,
        CME,
        CBOT,
        NYMEX,
        COMEX
    }

    public enum InstrumentType
    {
        Future,
        Stock,
        Index,
        Forex,
        Cfd,
        Cryptocurrency
    }

    public enum MarketPosition
    {
        Flat,
        Long,
        Short
    }

    public enum OrderState
    {
        Initialized,
        Submitted,
        Accepted,
        Working,
        PartFilled,
        Filled,
        Cancelled,
        Rejected,
        Unknown
    }

    public class ConnectOptions
    {
        public string Name { get; set; }
        public Provider Provider { get; set; }
        public Mode Mode { get; set; }
    }

    public class Connection
    {
        public static readonly List<Connection> Connections = new List<Connection>();

#pragma warning disable 67 // stub-only event; present to mirror the real host surface
        public static event EventHandler<ConnectionStatusEventArgs> ConnectionStatusUpdate;
#pragma warning restore 67

        public ConnectOptions Options { get; set; }
        public ConnectionStatus Status { get; set; }
        public ConnectionStatus PriceStatus { get; set; }
    }

    public class ConnectionStatusEventArgs : EventArgs
    {
        public Connection Connection { get; set; }
        public ConnectionStatus Status { get; set; }
        public ConnectionStatus PriceStatus { get; set; }
    }

    public class MasterInstrument
    {
        private string _name;
        private InstrumentType _instrumentType;
        private Currency _currency;

        public string Name
        {
            get
            {
                if (ThrowOnNameRead)
                    throw new InvalidOperationException("Offline master name read failure");
                return _name;
            }
            set { _name = value; }
        }

        public InstrumentType InstrumentType
        {
            get
            {
                if (ThrowOnInstrumentTypeRead)
                    throw new InvalidOperationException("Offline instrument type read failure");
                return _instrumentType;
            }
            set { _instrumentType = value; }
        }

        public Currency Currency
        {
            get
            {
                if (ThrowOnCurrencyRead)
                    throw new InvalidOperationException("Offline currency read failure");
                return _currency;
            }
            set { _currency = value; }
        }

        // Offline-only fault injection; not NinjaTrader API members. They let an offline test
        // prove an unreadable metadata read stays a recorded read failure rather than
        // defaulting to an empty string / zero.
        public bool ThrowOnNameRead { get; set; }
        public bool ThrowOnInstrumentTypeRead { get; set; }
        public bool ThrowOnCurrencyRead { get; set; }
        public bool ThrowOnTickSizeRead { get; set; }
        public bool ThrowOnPointValueRead { get; set; }
        private double _tickSize;
        public double TickSize
        {
            get
            {
                if (ThrowOnTickSizeRead)
                    throw new InvalidOperationException("Offline tick size read failure");
                return _tickSize;
            }
            set { _tickSize = value; }
        }

        private double _pointValue;
        public double PointValue
        {
            get
            {
                if (ThrowOnPointValueRead)
                    throw new InvalidOperationException("Offline point value read failure");
                return _pointValue;
            }
            set { _pointValue = value; }
        }

        public List<Exchange> Exchanges { get; set; } = new List<Exchange>();
    }

    public class Instrument
    {
        public string FullName { get; set; }
        public DateTime Expiry { get; set; }
        public Exchange Exchange { get; set; }
        public MasterInstrument MasterInstrument { get; set; }

        /// <summary>
        /// The instruments NinjaTrader currently has loaded (the discovery surface an AddOn can
        /// read). Offline the collection is empty; a test populates it explicitly.
        /// </summary>
        public static readonly System.Collections.ObjectModel.Collection<Instrument> All =
            new System.Collections.ObjectModel.Collection<Instrument>();

        /// <summary>
        /// Level I market data surface. Mirrors the public <c>Instrument.MarketData</c> object:
        /// a read-only snapshot plus an <c>Update</c> event. Offline the snapshot is empty and the
        /// event is only raised when a test raises it.
        /// </summary>
        public MarketData MarketData { get; } = new MarketData();

        /// <summary>
        /// Level II market-depth surface. Mirrors the public <c>Instrument.MarketDepth</c> object:
        /// bid/ask ladders plus an <c>Update</c> event.
        /// </summary>
        public MarketDepth MarketDepth { get; } = new MarketDepth();

        /// <summary>
        /// The instrument's dispatcher. Mirrors the public <c>Instrument.Dispatcher</c> surface,
        /// which the host marshals event subscription through. Offline it runs posted work inline.
        /// </summary>
        public System.Windows.Threading.Dispatcher Dispatcher { get; set; } =
            new System.Windows.Threading.Dispatcher();

        public static Instrument GetInstrument(string fullName)
        {
            if (string.IsNullOrEmpty(fullName))
            {
                return null;
            }

            foreach (var instrument in All)
            {
                if (instrument != null && string.Equals(instrument.FullName, fullName, StringComparison.Ordinal))
                {
                    return instrument;
                }
            }

            return null;
        }
    }

    /// <summary>
    /// Offline stub of the public Level I market-data type. Only the members the diagnostic
    /// adapter reads are declared; the event is a plain event a test can raise.
    /// </summary>
    public class MarketData
    {
#pragma warning disable 67 // stub-only event; present to mirror the real host surface
        public event EventHandler<NinjaTrader.Data.MarketDataEventArgs> Update;
#pragma warning restore 67

        public NinjaTrader.Data.MarketDataEventArgs Bid { get; set; } = new NinjaTrader.Data.MarketDataEventArgs();
        public NinjaTrader.Data.MarketDataEventArgs Ask { get; set; } = new NinjaTrader.Data.MarketDataEventArgs();
        public NinjaTrader.Data.MarketDataEventArgs Last { get; set; } = new NinjaTrader.Data.MarketDataEventArgs();

        /// <summary>Raises the <see cref="Update"/> event (test helper; mirrors a host callback).</summary>
        public void RaiseUpdate(NinjaTrader.Data.MarketDataEventArgs args)
        {
            var handler = Update;
            if (handler != null)
            {
                handler(this, args);
            }
        }
    }

    /// <summary>
    /// Offline stub of the public Level II market-depth type. Only the members the diagnostic
    /// adapter reads are declared; the event is a plain event a test can raise.
    /// </summary>
    public class MarketDepth
    {
#pragma warning disable 67 // stub-only event; present to mirror the real host surface
        public event EventHandler<NinjaTrader.Data.MarketDepthEventArgs> Update;
#pragma warning restore 67

        public Instrument Instrument { get; set; }

        public System.Collections.Generic.List<MarketDepthLevel> Bids { get; } =
            new System.Collections.Generic.List<MarketDepthLevel>();

        public System.Collections.Generic.List<MarketDepthLevel> Asks { get; } =
            new System.Collections.Generic.List<MarketDepthLevel>();

        /// <summary>Raises the <see cref="Update"/> event (test helper; mirrors a host callback).</summary>
        public void RaiseUpdate(NinjaTrader.Data.MarketDepthEventArgs args)
        {
            var handler = Update;
            if (handler != null)
            {
                handler(this, args);
            }
        }
    }

    /// <summary>Offline stub of one market-depth ladder level.</summary>
    public class MarketDepthLevel
    {
        public double Price { get; set; }
        public long Volume { get; set; }
    }

    public class Position
    {
        public Instrument Instrument { get; set; }
        public MarketPosition MarketPosition { get; set; }
        public int Quantity { get; set; }
        public double AveragePrice { get; set; }
    }

    public class Order
    {
        public string OrderId { get; set; }
        public Instrument Instrument { get; set; }
        public OrderState OrderState { get; set; }
        public int Quantity { get; set; }
    }

    public class Account
    {
        public static readonly Collection<Account> All = new Collection<Account>();

        public string Name { get; set; }
        public Provider Provider { get; set; }
        public Connection Connection { get; set; }
        // Offline-only fault injection; not a NinjaTrader API member.
        public bool ThrowOnDenominationRead { get; set; }
        private Currency _denomination;
        public Currency Denomination
        {
            get
            {
                if (ThrowOnDenominationRead)
                    throw new InvalidOperationException("Offline denomination read failure");
                return _denomination;
            }
            set { _denomination = value; }
        }
        // Offline-only fault injection; not NinjaTrader API members. They let an offline test
        // prove a Portfolio collection was never read (the getter throws on access).
        public bool ThrowOnPositionsRead { get; set; }
        public bool ThrowOnOrdersRead { get; set; }
        private readonly Collection<Position> _positions = new Collection<Position>();
        public Collection<Position> Positions
        {
            get
            {
                if (ThrowOnPositionsRead)
                    throw new InvalidOperationException("Offline positions read failure");
                return _positions;
            }
        }
        private readonly Collection<Order> _orders = new Collection<Order>();
        public Collection<Order> Orders
        {
            get
            {
                if (ThrowOnOrdersRead)
                    throw new InvalidOperationException("Offline orders read failure");
                return _orders;
            }
        }
    }
}

namespace NinjaTrader.Code
{
    public static class Output
    {
        public static void Process(string message, NinjaTrader.NinjaScript.PrintTo printTo) { }
        public static void Reset() { }
    }
}

namespace System.Windows
{
    public class Window
    {
        public System.Windows.Threading.Dispatcher Dispatcher { get; set; } =
            new System.Windows.Threading.Dispatcher();
        public bool IsLoaded { get; set; }
        public virtual void Show() { IsLoaded = true; }
        public virtual void Close() { IsLoaded = false; }
    }
}

namespace System.Windows.Threading
{
    public class Dispatcher
    {
        /// <summary>
        /// Offline-only switch (not a NinjaTrader/WPF API member) that lets a test choose whether
        /// the current thread is the dispatcher thread. Defaults to true so the stub keeps running
        /// posted work inline.
        /// </summary>
        public bool OnDispatcherThread { get; set; } = true;

        /// <summary>Offline-only switch that mirrors a dispatcher that has begun shutting down.</summary>
        public bool HasShutdownStarted { get; set; }

        /// <summary>Offline-only switch that mirrors a dispatcher that has finished shutting down.</summary>
        public bool HasShutdownFinished { get; set; }

        /// <summary>
        /// Offline-only switch that makes <see cref="InvokeAsync"/> throw as a real WPF dispatcher
        /// does once it is shut down, so a post that races the shutdown can be exercised.
        /// </summary>
        public bool ThrowOnInvokeAsync { get; set; }

        public bool CheckAccess() { return OnDispatcherThread; }

        public void InvokeAsync(Action action)
        {
            if (ThrowOnInvokeAsync)
            {
                throw new InvalidOperationException("Offline dispatcher has shut down");
            }

            if (action != null)
            {
                action();
            }
        }
    }
}

namespace NinjaTrader.Data
{
    /// <summary>Offline stub of the public Level I market-data event arguments.</summary>
    public class MarketDataEventArgs : EventArgs
    {
        public NinjaTrader.Cbi.Instrument Instrument { get; set; }
        public MarketDataType MarketDataType { get; set; }
        public double Price { get; set; }
        public double Bid { get; set; }
        public double Ask { get; set; }
        public long Volume { get; set; }
        public DateTime Time { get; set; }
        public bool IsReset { get; set; }
    }

    /// <summary>Offline stub of the public Level II market-depth event arguments.</summary>
    public class MarketDepthEventArgs : EventArgs
    {
        public NinjaTrader.Cbi.Instrument Instrument { get; set; }
        public MarketDataType MarketDataType { get; set; }
        public Operation Operation { get; set; }
        public int Position { get; set; }
        public double Price { get; set; }
        public long Volume { get; set; }
        public DateTime Time { get; set; }
        public bool IsReset { get; set; }
    }

    public enum MarketDataType
    {
        Ask,
        Bid,
        Last,
        DailyHigh,
        DailyLow,
        DailyVolume,
        LastClose,
        Opening,
        OpenInterest,
        Settlement
    }

    public enum Operation
    {
        Add,
        Update,
        Remove
    }
}

#endif
