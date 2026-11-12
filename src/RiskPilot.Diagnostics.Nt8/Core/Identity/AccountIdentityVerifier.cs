using System;
using System.Collections.Generic;
using RiskPilot.Diagnostics.Nt8.Core.Models.Observations;
using RiskPilot.Diagnostics.Nt8.Core.Status;

namespace RiskPilot.Diagnostics.Nt8.Core.Identity
{
    /// <summary>
    /// The permanent operational identity RiskPilot.BOT requires (D-095/D-096). The literal
    /// account and connection names are Owner-provided configuration identifiers: they may
    /// appear in reviewed source and tests as explicit match constants, but they are never
    /// emitted to a log, a finding, a report or an evidence artifact. Everything the verifier
    /// reports is a reason code, a count or a boolean.
    /// </summary>
    public static class ExpectedAccountIdentity
    {
        /// <summary>Exact account name (ordinal match; no partial or similar-name match).</summary>
        public const string AccountName = "SimRP.BOT";

        /// <summary>Exact connection name (ordinal match; no partial or similar-name match).</summary>
        public const string ConnectionName = "My Rithmic";

        /// <summary>Required provider for the account and for its connection.</summary>
        public const string Provider = "Rithmic";

        /// <summary>Required connection mode, as the host enum renders it.</summary>
        public const string SimulationMode = "SIMULATION";

        /// <summary>Required account denomination, as the host enum renders it.</summary>
        public const string Currency = "USD";

        /// <summary>Account name match is exact and case-sensitive.</summary>
        public static bool AccountNameMatches(string observed)
        {
            return string.Equals(observed, AccountName, StringComparison.Ordinal);
        }

        /// <summary>Connection name match is exact and case-sensitive.</summary>
        public static bool ConnectionNameMatches(string observed)
        {
            return string.Equals(observed, ConnectionName, StringComparison.Ordinal);
        }

        /// <summary>
        /// Provider match. The provider enum has no guaranteed Rithmic member, so the host
        /// renders it as a string; that rendering is compared case-insensitively (RiskPilot.BOT
        /// PR #275).
        /// </summary>
        public static bool ProviderMatches(string observed)
        {
            return string.Equals(observed, Provider, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>True when the rendered connection mode is the required simulation mode.</summary>
        public static bool ModeIsSimulation(string observedMode)
        {
            return string.Equals(observedMode, SimulationMode, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>True when the rendered denomination is the required currency.</summary>
        public static bool CurrencyIsUsd(string observedCurrency)
        {
            return string.Equals(observedCurrency, Currency, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>Reason codes emitted by the account identity verifier. Stable wire values.</summary>
    public static class AccountIdentityReasonCodes
    {
        public const string Match = "ACCOUNT_IDENTITY_MATCH";
        public const string Unobservable = "ACCOUNT_IDENTITY_UNOBSERVABLE";
        public const string StubOrMock = "ACCOUNT_IDENTITY_VALUE_FROM_STUB_OR_MOCK";
        public const string NoAccountObserved = "ACCOUNT_IDENTITY_NO_ACCOUNT_OBSERVED";
        public const string Ambiguous = "ACCOUNT_IDENTITY_AMBIGUOUS";
        public const string NameMismatch = "ACCOUNT_NAME_MISMATCH";
        public const string AccountProviderMismatch = "ACCOUNT_PROVIDER_MISMATCH";
        public const string AccountProviderUnobservable = "ACCOUNT_PROVIDER_UNOBSERVABLE";
        public const string ConnectionNotObserved = "CONNECTION_NOT_OBSERVED";
        public const string ConnectionNameMismatch = "CONNECTION_NAME_MISMATCH";
        public const string ConnectionNameUnobservable = "CONNECTION_NAME_UNOBSERVABLE";
        public const string ConnectionProviderMismatch = "CONNECTION_PROVIDER_MISMATCH";
        public const string ConnectionProviderUnobservable = "CONNECTION_PROVIDER_UNOBSERVABLE";
        public const string ConnectionStatusUnobservable = "CONNECTION_STATUS_UNOBSERVABLE";
        public const string ConnectionNotConnected = "CONNECTION_NOT_CONNECTED";
        public const string ModeMismatch = "MODE_MISMATCH";
        public const string ModeUnobservable = "MODE_UNOBSERVABLE";
        public const string CurrencyMismatch = "CURRENCY_MISMATCH";
        public const string CurrencyUnobservable = "CURRENCY_UNOBSERVABLE";
        public const string IncompleteMetadata = "ACCOUNT_IDENTITY_INCOMPLETE_METADATA";
    }

    /// <summary>
    /// Outcome of an exact-identity comparison. It carries no raw identity: only the status,
    /// the reason codes, the field-level detail, and the counts a reader needs to judge the
    /// result.
    /// </summary>
    public sealed class AccountIdentityVerdict
    {
        public DiagnosticStatus Status { get; private set; }

        /// <summary>Reason codes, most significant first (for example the primary mismatch).</summary>
        public IList<string> ReasonCodes { get; private set; }

        /// <summary>Fields that were observed and disagreed with the required identity.</summary>
        public IList<string> MismatchedFields { get; private set; }

        /// <summary>Required fields that could not be observed, so identity cannot be proven.</summary>
        public IList<string> UnobservableFields { get; private set; }

        public int ObservedAccountCount { get; private set; }

        public int MatchingAccountCount { get; private set; }

        /// <summary>Synthetic account reference, or empty when no account was observable.</summary>
        public string AccountRef { get; private set; }

        private AccountIdentityVerdict()
        {
            ReasonCodes = new List<string>();
            MismatchedFields = new List<string>();
            UnobservableFields = new List<string>();
            AccountRef = string.Empty;
        }

        internal static AccountIdentityVerdict Create(
            DiagnosticStatus status,
            AccountIdentityObservation observation,
            IList<string> reasonCodes,
            IList<string> mismatchedFields,
            IList<string> unobservableFields)
        {
            return new AccountIdentityVerdict
            {
                Status = status,
                ReasonCodes = new List<string>(reasonCodes ?? new List<string>()),
                MismatchedFields = new List<string>(mismatchedFields ?? new List<string>()),
                UnobservableFields = new List<string>(unobservableFields ?? new List<string>()),
                ObservedAccountCount = observation != null ? observation.ObservedAccountCount : 0,
                MatchingAccountCount = observation != null ? observation.MatchingAccountCount : 0,
                AccountRef = observation != null ? (observation.AccountRef ?? string.Empty) : string.Empty
            };
        }
    }

    /// <summary>
    /// Compares the observed account identity against the permanent RiskPilot.BOT identity.
    /// <para>
    /// The comparison is fail-closed and never accepts a partial match:
    /// <list type="bullet">
    /// <item>a missing or unreadable account surface is INCONCLUSIVE;</item>
    /// <item>an observed account whose name is not exactly the expected name is FAIL (a similar
    /// or differently-cased name is not a match);</item>
    /// <item>more than one exact name match is INCONCLUSIVE (ambiguous);</item>
    /// <item>an observed field that contradicts the expected identity is FAIL;</item>
    /// <item>a required field that could not be observed is INCONCLUSIVE;</item>
    /// <item>only a fully observed, fully matching identity is PASS.</item>
    /// </list>
    /// </para>
    /// </summary>
    public static class AccountIdentityVerifier
    {
        public static AccountIdentityVerdict Evaluate(AccountIdentityObservation observation)
        {
            if (observation == null || !observation.AccountSurfaceObservable)
            {
                return Inconclusive(observation, AccountIdentityReasonCodes.Unobservable);
            }

            if (observation.IsStubOrMockValue)
            {
                return Inconclusive(observation, AccountIdentityReasonCodes.StubOrMock);
            }

            if (observation.ObservedAccountCount <= 0)
            {
                return Inconclusive(observation, AccountIdentityReasonCodes.NoAccountObserved);
            }

            if (observation.MatchingAccountCount == 0)
            {
                return Fail(observation, AccountIdentityReasonCodes.NameMismatch);
            }

            if (observation.MatchingAccountCount > 1)
            {
                return Inconclusive(observation, AccountIdentityReasonCodes.Ambiguous);
            }

            var mismatched = new List<string>();
            var unobservable = new List<string>();

            if (!observation.AccountProviderObservable)
            {
                unobservable.Add(AccountIdentityReasonCodes.AccountProviderUnobservable);
            }
            else if (!observation.AccountProviderMatches)
            {
                mismatched.Add(AccountIdentityReasonCodes.AccountProviderMismatch);
            }

            if (!observation.ConnectionObserved)
            {
                unobservable.Add(AccountIdentityReasonCodes.ConnectionNotObserved);
            }
            else
            {
                if (!observation.ConnectionNameObservable)
                {
                    unobservable.Add(AccountIdentityReasonCodes.ConnectionNameUnobservable);
                }
                else if (!observation.ConnectionNameMatches)
                {
                    mismatched.Add(AccountIdentityReasonCodes.ConnectionNameMismatch);
                }

                if (!observation.ConnectionProviderObservable)
                {
                    unobservable.Add(AccountIdentityReasonCodes.ConnectionProviderUnobservable);
                }
                else if (!observation.ConnectionProviderMatches)
                {
                    mismatched.Add(AccountIdentityReasonCodes.ConnectionProviderMismatch);
                }

                if (!observation.ConnectionStatusObservable)
                {
                    unobservable.Add(AccountIdentityReasonCodes.ConnectionStatusUnobservable);
                }
                else if (!observation.ConnectionConnected)
                {
                    unobservable.Add(AccountIdentityReasonCodes.ConnectionNotConnected);
                }
            }

            if (!observation.ModeObservable)
            {
                unobservable.Add(AccountIdentityReasonCodes.ModeUnobservable);
            }
            else if (!observation.ModeIsSimulation)
            {
                mismatched.Add(AccountIdentityReasonCodes.ModeMismatch);
            }

            if (!observation.DenominationObservable)
            {
                unobservable.Add(AccountIdentityReasonCodes.CurrencyUnobservable);
            }
            else if (!observation.DenominationIsUsd)
            {
                mismatched.Add(AccountIdentityReasonCodes.CurrencyMismatch);
            }

            // A definite contradiction outranks an unobservable field: the identity is proven
            // wrong, not merely unproven.
            if (mismatched.Count > 0)
            {
                return AccountIdentityVerdict.Create(
                    DiagnosticStatus.FAIL, observation, mismatched, mismatched, unobservable);
            }

            if (unobservable.Count > 0)
            {
                return AccountIdentityVerdict.Create(
                    DiagnosticStatus.INCONCLUSIVE, observation,
                    Prefix(AccountIdentityReasonCodes.IncompleteMetadata, unobservable),
                    mismatched, unobservable);
            }

            return AccountIdentityVerdict.Create(
                DiagnosticStatus.PASS, observation,
                new List<string> { AccountIdentityReasonCodes.Match },
                mismatched, unobservable);
        }

        private static AccountIdentityVerdict Inconclusive(AccountIdentityObservation observation, string reasonCode)
        {
            return AccountIdentityVerdict.Create(
                DiagnosticStatus.INCONCLUSIVE, observation,
                new List<string> { reasonCode }, new List<string>(), new List<string>());
        }

        private static AccountIdentityVerdict Fail(AccountIdentityObservation observation, string reasonCode)
        {
            return AccountIdentityVerdict.Create(
                DiagnosticStatus.FAIL, observation,
                new List<string> { reasonCode }, new List<string> { reasonCode }, new List<string>());
        }

        private static IList<string> Prefix(string first, IList<string> rest)
        {
            var list = new List<string> { first };
            if (rest != null)
            {
                list.AddRange(rest);
            }

            return list;
        }
    }
}
