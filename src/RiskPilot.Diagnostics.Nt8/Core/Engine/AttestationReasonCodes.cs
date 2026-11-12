namespace RiskPilot.Diagnostics.Nt8.Core.Engine
{
    /// <summary>
    /// Canonical attestation failure cause codes. These mirror the codes RiskPilot.BOT uses
    /// so the diagnostic tool and the trading repository speak the same language, without
    /// the tool taking any dependency on BOT code. Only the subset the tool can reproduce
    /// from read-only public evidence is defined here.
    /// </summary>
    public static class AttestationReasonCodes
    {
        public const string HostUnavailable = "HOST_UNAVAILABLE";
        public const string HostReadFailed = "HOST_READ_FAILED";
        public const string IdentityUnparseable = "IDENTITY_UNPARSEABLE";
        public const string IdentityMismatch = "IDENTITY_MISMATCH";
        public const string InstrumentRootMismatch = "INSTRUMENT_ROOT_MISMATCH";
        public const string InstrumentTypeMismatch = "INSTRUMENT_TYPE_MISMATCH";
        public const string ExchangeUnmapped = "EXCHANGE_UNMAPPED";
        public const string CurrencyUnmapped = "CURRENCY_UNMAPPED";
        public const string TickSizeMismatch = "TICK_SIZE_MISMATCH";
        public const string ContractMultiplierMismatch = "CONTRACT_MULTIPLIER_MISMATCH";
        public const string ExpiryMismatch = "EXPIRY_MISMATCH";
        public const string RegistryEntryNotApproved = "REGISTRY_ENTRY_NOT_APPROVED";

        /// <summary>D-078 operator field-approval gate: no operator PASS is recorded.</summary>
        public const string D078FieldApprovalNotApproved = "D078_FIELD_APPROVAL_NOT_APPROVED";

        /// <summary>The instrument specification could not be resolved for the observed contract.</summary>
        public const string MissingInstrumentSpecification = "MISSING_INSTRUMENT_SPECIFICATION";
    }
}
