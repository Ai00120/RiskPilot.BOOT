namespace RiskPilot.Diagnostics.Nt8.Core.Models
{
    /// <summary>
    /// Trust level of a record's source timestamp. The recorded baseline evidence was
    /// captured with an unverified server clock, so that is the default and the only
    /// value the tool asserts without independent proof.
    /// </summary>
    public enum SourceTimeBasis
    {
        Unknown = 0,
        ServerTimeUnverified = 1,
        LocalHostClock = 2,
        ExchangeTimeVerified = 3
    }

    /// <summary>Logical role a connection plays. NT8 separates price and order feeds.</summary>
    public enum ConnectionRole
    {
        Unknown = 0,
        Price = 1,
        Order = 2,
        Broker = 3
    }

    public enum ConnectionState
    {
        Unknown = 0,
        Disconnected = 1,
        Connecting = 2,
        Connected = 3,
        ConnectionLost = 4
    }

    /// <summary>Environment class observed at runtime (not the historical baseline).</summary>
    public enum EnvironmentClass
    {
        Unknown = 0,
        Simulation = 1,
        Live = 2
    }

    public enum MarketDataKind
    {
        Unknown = 0,
        Quote = 1,
        Trade = 2,
        Bar = 3
    }

    public enum DomOperation
    {
        Unknown = 0,
        Insert = 1,
        Update = 2,
        Remove = 3,
        Reset = 4
    }

    public enum PositionSide
    {
        Flat = 0,
        Long = 1,
        Short = 2
    }

    /// <summary>Direction of a trade print, when one could be determined.</summary>
    public enum TradeDirection
    {
        Unknown = 0,
        Buy = 1,
        Sell = 2
    }

    /// <summary>
    /// Provenance of a trade print's direction. A direction derived by comparing the print
    /// price to a known bid/ask is an inference and is never reported as provider-confirmed.
    /// </summary>
    public enum TradeDirectionSource
    {
        Unspecified = 0,
        ProviderSupplied = 1,
        DerivedFromQuote = 2
    }

    /// <summary>Exact reason a trade direction could not be determined.</summary>
    public enum TradeDirectionUnknownReason
    {
        NotApplicable = 0,
        ProviderReportedUnknown = 1,
        NoBboAvailable = 2,
        BboIncomplete = 3,
        PriceInsideSpread = 4,
        BboInvalid = 5
    }

    /// <summary>Normalized order lifecycle state (read-only mirror of NT8 OrderState).</summary>
    public enum OrderLifecycleState
    {
        Unknown = 0,
        Initialized = 1,
        Submitted = 2,
        Accepted = 3,
        Working = 4,
        PartFilled = 5,
        Filled = 6,
        Cancelled = 7,
        Rejected = 8,
        ChangeSubmitted = 9
    }

    /// <summary>
    /// Activity class of an order, derived from its lifecycle state. It answers the question
    /// "is this order still live at the host, or is it finished?" so that a historical or
    /// completed order is never counted as active. <see cref="Unknown"/> is fail-closed: an
    /// order whose state could not be classified makes an active-order count unreliable.
    /// </summary>
    public enum OrderActivityClass
    {
        /// <summary>The lifecycle state was not classifiable; the count cannot be trusted.</summary>
        Unknown = 0,

        /// <summary>Still live at the host: working, accepted, pending submit or partially filled.</summary>
        Active = 1,

        /// <summary>Reached the Filled state; no longer active.</summary>
        Completed = 2,

        /// <summary>Reached the Cancelled state; no longer active.</summary>
        Cancelled = 3,

        /// <summary>Reached the Rejected state; never became active.</summary>
        Rejected = 4
    }

    public enum StrategyActivity
    {
        Unknown = 0,
        Inactive = 1,
        Active = 2,
        Stopped = 3
    }

    /// <summary>
    /// Classification of an order identifier as observed from the host. The categories
    /// are deliberately distinct so that a missing identifier is never conflated with an
    /// empty or whitespace one, and an identifier that could not be read at all is
    /// distinguished from one the host reported as absent.
    /// </summary>
    public enum OrderIdClass
    {
        /// <summary>The identifier was not read (no observation was attempted).</summary>
        Unavailable = 0,

        /// <summary>The identifier field was null.</summary>
        Null = 1,

        /// <summary>The identifier field was the empty string.</summary>
        Empty = 2,

        /// <summary>The identifier field contained only whitespace.</summary>
        Whitespace = 3,

        /// <summary>The identifier field contained a non-empty, non-whitespace value.</summary>
        Valid = 4
    }

    /// <summary>Classification of an error surfaced by the diagnostic pipeline.</summary>
    public enum ErrorClass
    {
        None = 0,
        HostUnavailable = 1,
        DataUnavailable = 2,
        SequenceGap = 3,
        DuplicateEvent = 4,
        QueueOverflow = 5,
        TimestampInvalid = 6,
        SerializationFailure = 7,
        ConfigurationInvalid = 8,
        Unexpected = 9,
        CorrelationIncomplete = 10,
        MissingInstrumentSpecification = 11,
        ApprovalRequired = 12
    }

    public static class ModelEnums
    {
        public static string ToWire(this SourceTimeBasis basis)
        {
            switch (basis)
            {
                case SourceTimeBasis.ServerTimeUnverified:
                    return "SERVER_TIME_UNVERIFIED";
                case SourceTimeBasis.LocalHostClock:
                    return "LOCAL_HOST_CLOCK";
                case SourceTimeBasis.ExchangeTimeVerified:
                    return "EXCHANGE_TIME_VERIFIED";
                default:
                    return "UNKNOWN";
            }
        }

        public static string ToWire(this ConnectionRole role)
        {
            switch (role)
            {
                case ConnectionRole.Price:
                    return "PRICE";
                case ConnectionRole.Order:
                    return "ORDER";
                case ConnectionRole.Broker:
                    return "BROKER";
                default:
                    return "UNKNOWN";
            }
        }

        public static string ToWire(this ConnectionState state)
        {
            switch (state)
            {
                case ConnectionState.Disconnected:
                    return "DISCONNECTED";
                case ConnectionState.Connecting:
                    return "CONNECTING";
                case ConnectionState.Connected:
                    return "CONNECTED";
                case ConnectionState.ConnectionLost:
                    return "CONNECTION_LOST";
                default:
                    return "UNKNOWN";
            }
        }

        public static string ToWire(this EnvironmentClass environment)
        {
            switch (environment)
            {
                case EnvironmentClass.Simulation:
                    return "SIMULATION";
                case EnvironmentClass.Live:
                    return "LIVE";
                default:
                    return "UNKNOWN";
            }
        }

        public static string ToWire(this MarketDataKind kind)
        {
            switch (kind)
            {
                case MarketDataKind.Quote:
                    return "QUOTE";
                case MarketDataKind.Trade:
                    return "TRADE";
                case MarketDataKind.Bar:
                    return "BAR";
                default:
                    return "UNKNOWN";
            }
        }

        public static string ToWire(this DomOperation operation)
        {
            switch (operation)
            {
                case DomOperation.Insert:
                    return "INSERT";
                case DomOperation.Update:
                    return "UPDATE";
                case DomOperation.Remove:
                    return "REMOVE";
                case DomOperation.Reset:
                    return "RESET";
                default:
                    return "UNKNOWN";
            }
        }

        public static string ToWire(this PositionSide side)
        {
            switch (side)
            {
                case PositionSide.Long:
                    return "LONG";
                case PositionSide.Short:
                    return "SHORT";
                default:
                    return "FLAT";
            }
        }

        public static string ToWire(this OrderLifecycleState state)
        {
            return state.ToString().ToUpperInvariant();
        }

        public static string ToWire(this StrategyActivity activity)
        {
            return activity.ToString().ToUpperInvariant();
        }

        public static string ToWire(this OrderActivityClass activityClass)
        {
            return activityClass.ToString().ToUpperInvariant();
        }

        public static string ToWire(this OrderIdClass orderIdClass)
        {
            switch (orderIdClass)
            {
                case OrderIdClass.Null:
                    return "NULL";
                case OrderIdClass.Empty:
                    return "EMPTY";
                case OrderIdClass.Whitespace:
                    return "WHITESPACE";
                case OrderIdClass.Valid:
                    return "VALID";
                default:
                    return "UNAVAILABLE";
            }
        }

        public static string ToWire(this ErrorClass errorClass)
        {
            switch (errorClass)
            {
                case ErrorClass.HostUnavailable:
                    return "HOST_UNAVAILABLE";
                case ErrorClass.DataUnavailable:
                    return "DATA_UNAVAILABLE";
                case ErrorClass.SequenceGap:
                    return "SEQUENCE_GAP";
                case ErrorClass.DuplicateEvent:
                    return "DUPLICATE_EVENT";
                case ErrorClass.QueueOverflow:
                    return "QUEUE_OVERFLOW";
                case ErrorClass.TimestampInvalid:
                    return "TIMESTAMP_INVALID";
                case ErrorClass.SerializationFailure:
                    return "SERIALIZATION_FAILURE";
                case ErrorClass.ConfigurationInvalid:
                    return "CONFIGURATION_INVALID";
                case ErrorClass.CorrelationIncomplete:
                    return "CORRELATION_INCOMPLETE";
                case ErrorClass.MissingInstrumentSpecification:
                    return "MISSING_INSTRUMENT_SPECIFICATION";
                case ErrorClass.ApprovalRequired:
                    return "APPROVAL_REQUIRED";
                case ErrorClass.Unexpected:
                    return "UNEXPECTED_ERROR";
                default:
                    return "NONE";
            }
        }
    }
}
