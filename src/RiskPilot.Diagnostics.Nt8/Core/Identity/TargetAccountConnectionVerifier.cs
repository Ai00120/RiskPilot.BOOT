using System.Collections.Generic;
using RiskPilot.Diagnostics.Nt8.Core.Models.Observations;
using RiskPilot.Diagnostics.Nt8.Core.Status;

namespace RiskPilot.Diagnostics.Nt8.Core.Identity
{
    /// <summary>Reason codes emitted by the target-account association verifier. Stable wire values.</summary>
    public static class TargetAccountConnectionReasonCodes
    {
        public const string Match = "TARGET_ACCOUNT_CONNECTION_MATCH";
        public const string Unobservable = "TARGET_ACCOUNT_ASSOCIATION_UNOBSERVABLE";
        public const string StubOrMock = "TARGET_ACCOUNT_ASSOCIATION_VALUE_FROM_STUB_OR_MOCK";
        public const string NoAccountObserved = "TARGET_ACCOUNT_NO_ACCOUNT_OBSERVED";
        public const string NameMismatch = "TARGET_ACCOUNT_NAME_MISMATCH";
        public const string AmbiguousAccount = "TARGET_ACCOUNT_ASSOCIATION_AMBIGUOUS";
        public const string AmbiguousConnection = "TARGET_ACCOUNT_CONNECTION_AMBIGUOUS";
        public const string AccountProviderMismatch = "TARGET_ACCOUNT_PROVIDER_MISMATCH";
        public const string AccountProviderUnobservable = "TARGET_ACCOUNT_PROVIDER_UNOBSERVABLE";
        public const string ConnectionNotObserved = "TARGET_ACCOUNT_CONNECTION_NOT_OBSERVED";
        public const string ConnectionNameMismatch = "TARGET_ACCOUNT_CONNECTION_NAME_MISMATCH";
        public const string ConnectionNameUnobservable = "TARGET_ACCOUNT_CONNECTION_NAME_UNOBSERVABLE";
        public const string ConnectionProviderMismatch = "TARGET_ACCOUNT_CONNECTION_PROVIDER_MISMATCH";
        public const string ConnectionProviderUnobservable = "TARGET_ACCOUNT_CONNECTION_PROVIDER_UNOBSERVABLE";
        public const string IncompleteMetadata = "TARGET_ACCOUNT_ASSOCIATION_INCOMPLETE_METADATA";
    }

    /// <summary>
    /// Outcome of the target-account association comparison. It carries no raw identity: only the
    /// status, the reason codes, the field-level detail and the counts a reader needs to judge the
    /// result.
    /// </summary>
    public sealed class TargetAccountConnectionVerdict
    {
        public DiagnosticStatus Status { get; private set; }

        /// <summary>Reason codes, most significant first (for example the primary mismatch).</summary>
        public IList<string> ReasonCodes { get; private set; }

        /// <summary>Fields that were observed and disagreed with the required association.</summary>
        public IList<string> MismatchedFields { get; private set; }

        /// <summary>Required fields that could not be observed, so the association cannot be proven.</summary>
        public IList<string> UnobservableFields { get; private set; }

        public int ObservedAccountCount { get; private set; }

        public int MatchingAccountCount { get; private set; }

        public int ConnectionCount { get; private set; }

        public int MatchingConnectionCount { get; private set; }

        /// <summary>Synthetic account reference, or empty when no account was observable.</summary>
        public string AccountRef { get; private set; }

        private TargetAccountConnectionVerdict()
        {
            ReasonCodes = new List<string>();
            MismatchedFields = new List<string>();
            UnobservableFields = new List<string>();
            AccountRef = string.Empty;
        }

        internal static TargetAccountConnectionVerdict Create(
            DiagnosticStatus status,
            TargetAccountConnectionObservation observation,
            IList<string> reasonCodes,
            IList<string> mismatchedFields,
            IList<string> unobservableFields)
        {
            return new TargetAccountConnectionVerdict
            {
                Status = status,
                ReasonCodes = new List<string>(reasonCodes ?? new List<string>()),
                MismatchedFields = new List<string>(mismatchedFields ?? new List<string>()),
                UnobservableFields = new List<string>(unobservableFields ?? new List<string>()),
                ObservedAccountCount = observation != null ? observation.ObservedAccountCount : 0,
                MatchingAccountCount = observation != null ? observation.MatchingAccountCount : 0,
                ConnectionCount = observation != null ? observation.ConnectionCount : 0,
                MatchingConnectionCount = observation != null ? observation.MatchingConnectionCount : 0,
                AccountRef = observation != null ? (observation.AccountRef ?? string.Empty) : string.Empty
            };
        }
    }

    /// <summary>
    /// Proves that the target account is bound to the required connection and provider, rather
    /// than accepting an arbitrary or first-available connection. The comparison is fail-closed
    /// and never accepts a partial match:
    /// <list type="bullet">
    /// <item>a missing or unreadable account surface is INCONCLUSIVE;</item>
    /// <item>an observed account whose name is not exactly the expected name is FAIL (a similar
    /// or differently-cased name is not a match);</item>
    /// <item>more than one exact account match is INCONCLUSIVE (ambiguous);</item>
    /// <item>more than one enumerated connection matching the required connection identity is
    /// INCONCLUSIVE (ambiguous association);</item>
    /// <item>an account that reports no associated connection is INCONCLUSIVE (the host cannot
    /// establish the relationship);</item>
    /// <item>an observed field that contradicts the required association is FAIL;</item>
    /// <item>a required field that could not be observed is INCONCLUSIVE;</item>
    /// <item>only a fully observed, fully matching association is PASS.</item>
    /// </list>
    /// </summary>
    public static class TargetAccountConnectionVerifier
    {
        public static TargetAccountConnectionVerdict Evaluate(TargetAccountConnectionObservation observation)
        {
            if (observation == null || !observation.AccountSurfaceObservable)
            {
                return Inconclusive(observation, TargetAccountConnectionReasonCodes.Unobservable);
            }

            if (observation.IsStubOrMockValue)
            {
                return Inconclusive(observation, TargetAccountConnectionReasonCodes.StubOrMock);
            }

            if (observation.ObservedAccountCount <= 0)
            {
                return Inconclusive(observation, TargetAccountConnectionReasonCodes.NoAccountObserved);
            }

            if (observation.MatchingAccountCount == 0)
            {
                return Fail(observation, TargetAccountConnectionReasonCodes.NameMismatch);
            }

            if (observation.MatchingAccountCount > 1)
            {
                return Inconclusive(observation, TargetAccountConnectionReasonCodes.AmbiguousAccount);
            }

            // More than one enumerated connection matches the required connection identity, so
            // the host cannot prove which one the target account is bound to.
            if (observation.MatchingConnectionCount > 1)
            {
                return Inconclusive(observation, TargetAccountConnectionReasonCodes.AmbiguousConnection);
            }

            var mismatched = new List<string>();
            var unobservable = new List<string>();

            if (!observation.AccountProviderObservable)
            {
                unobservable.Add(TargetAccountConnectionReasonCodes.AccountProviderUnobservable);
            }
            else if (!observation.AccountProviderMatches)
            {
                mismatched.Add(TargetAccountConnectionReasonCodes.AccountProviderMismatch);
            }

            if (!observation.ConnectionObserved)
            {
                unobservable.Add(TargetAccountConnectionReasonCodes.ConnectionNotObserved);
            }
            else
            {
                if (!observation.ConnectionNameObservable)
                {
                    unobservable.Add(TargetAccountConnectionReasonCodes.ConnectionNameUnobservable);
                }
                else if (!observation.ConnectionNameMatches)
                {
                    mismatched.Add(TargetAccountConnectionReasonCodes.ConnectionNameMismatch);
                }

                if (!observation.ConnectionProviderObservable)
                {
                    unobservable.Add(TargetAccountConnectionReasonCodes.ConnectionProviderUnobservable);
                }
                else if (!observation.ConnectionProviderMatches)
                {
                    mismatched.Add(TargetAccountConnectionReasonCodes.ConnectionProviderMismatch);
                }
            }

            // A definite contradiction outranks an unobservable field: the association is proven
            // wrong, not merely unproven.
            if (mismatched.Count > 0)
            {
                return TargetAccountConnectionVerdict.Create(
                    DiagnosticStatus.FAIL, observation, mismatched, mismatched, unobservable);
            }

            if (unobservable.Count > 0)
            {
                return TargetAccountConnectionVerdict.Create(
                    DiagnosticStatus.INCONCLUSIVE, observation,
                    Prefix(TargetAccountConnectionReasonCodes.IncompleteMetadata, unobservable),
                    mismatched, unobservable);
            }

            return TargetAccountConnectionVerdict.Create(
                DiagnosticStatus.PASS, observation,
                new List<string> { TargetAccountConnectionReasonCodes.Match },
                mismatched, unobservable);
        }

        private static TargetAccountConnectionVerdict Inconclusive(TargetAccountConnectionObservation observation, string reasonCode)
        {
            return TargetAccountConnectionVerdict.Create(
                DiagnosticStatus.INCONCLUSIVE, observation,
                new List<string> { reasonCode }, new List<string>(), new List<string>());
        }

        private static TargetAccountConnectionVerdict Fail(TargetAccountConnectionObservation observation, string reasonCode)
        {
            return TargetAccountConnectionVerdict.Create(
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
