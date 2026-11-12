namespace RiskPilot.Diagnostics.Nt8.Core.Models
{
    /// <summary>Read-only snapshot of one NinjaTrader connection and its feed roles.</summary>
    public sealed class ConnectionRecord : NormalizedRecord
    {
        /// <summary>Connection name as configured (not an account identity).</summary>
        public string ConnectionName { get; set; }

        public ConnectionState PriceState { get; set; }

        public ConnectionState OrderChannelState { get; set; }

        /// <summary>Provider identity resolved from the connection options.</summary>
        public string ProviderName { get; set; }

        /// <summary>True when the provider was identified by string comparison, not an enum.</summary>
        public bool ProviderDetectedByString { get; set; }

        public ConnectionRole Role { get; set; }

        /// <summary>Number of reconnects observed in this session for this connection.</summary>
        public int ReconnectCount { get; set; }

        public ConnectionRecord()
        {
            ConnectionName = string.Empty;
            ProviderName = string.Empty;
        }
    }
}
