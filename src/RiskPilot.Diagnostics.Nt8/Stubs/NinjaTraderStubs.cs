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

    public enum Provider
    {
        Default,
        Simulator,
        Playback,
        Rithmic,
        InteractiveBrokers,
        CQG
    }

    public enum Currency
    {
        UsDollar,
        Euro,
        Yen,
        BritishPound
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
        public string Name { get; set; }
        public InstrumentType InstrumentType { get; set; }
        public Currency Currency { get; set; }
        public double TickSize { get; set; }
        public double PointValue { get; set; }
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
        public Currency Denomination { get; set; }
        public Collection<Position> Positions { get; } = new Collection<Position>();
        public Collection<Order> Orders { get; } = new Collection<Order>();
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
        public bool CheckAccess() { return true; }
        public bool HasShutdownStarted { get { return false; } }
        public bool HasShutdownFinished { get { return false; } }

        public void InvokeAsync(Action action)
        {
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
