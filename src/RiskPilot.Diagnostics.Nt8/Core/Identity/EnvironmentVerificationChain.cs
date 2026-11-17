using System.Collections.Generic;
using RiskPilot.Diagnostics.Nt8.Core.Models;
using RiskPilot.Diagnostics.Nt8.Core.Models.Observations;
using RiskPilot.Diagnostics.Nt8.Core.Status;

namespace RiskPilot.Diagnostics.Nt8.Core.Identity
{
    /// <summary>
    /// Host-free evaluation of the environment verification chain
    /// <c>Target Account → Verified Connection → Provider Identity → Environment Classification</c>.
    /// <para>
    /// The environment class is only a proven fact once every link holds: the target account
    /// identity must be verified, its own connection association must be verified, the provider
    /// identity must be confirmed on both sides, and a class must have been established from that
    /// verified connection's mode. When any link is unresolved the chain is not complete and the
    /// class is never reported as a verified environment. This is the single derivation M1 and the
    /// exported <c>environment.json</c> both read, so a verified environment is never reported off
    /// the back of a partial chain.
    /// </para>
    /// </summary>
    public static class EnvironmentVerificationChain
    {
        /// <summary>Chain links, in evaluation order.</summary>
        public const string TargetAccountLink = "TARGET_ACCOUNT";

        public const string ConnectionAssociationLink = "CONNECTION_ASSOCIATION";
        public const string ProviderIdentityLink = "PROVIDER_IDENTITY";
        public const string EnvironmentClassificationLink = "ENVIRONMENT_CLASSIFICATION";

        /// <summary>Every link was proven and a class was established from the verified chain.</summary>
        public const string Verified = "ENVIRONMENT_CHAIN_VERIFIED";

        public const string AccountUnverified = "ENVIRONMENT_CHAIN_ACCOUNT_UNVERIFIED";
        public const string ConnectionUnverified = "ENVIRONMENT_CHAIN_CONNECTION_UNVERIFIED";
        public const string ProviderUnverified = "ENVIRONMENT_CHAIN_PROVIDER_UNVERIFIED";
        public const string EnvironmentUnknown = "ENVIRONMENT_CHAIN_CLASS_UNKNOWN";

        /// <summary>Result of evaluating the chain; carries only booleans and reason codes.</summary>
        public sealed class Result
        {
            public bool AccountVerified { get; internal set; }

            public bool ConnectionVerified { get; internal set; }

            public bool ProviderVerified { get; internal set; }

            /// <summary>True when a class (Simulation/Live) was established from the verified chain.</summary>
            public bool EnvironmentEstablished { get; internal set; }

            public IList<string> ReasonCodes { get; internal set; }

            /// <summary>True only when every link holds and the class is established.</summary>
            public bool FullyVerified
            {
                get { return AccountVerified && ConnectionVerified && ProviderVerified && EnvironmentEstablished; }
            }

            internal Result()
            {
                ReasonCodes = new List<string>();
            }
        }

        /// <summary>
        /// Evaluates the chain from the M2 account identity observation, the M2 target-connection
        /// association observation and the derived environment class. A null/unobservable
        /// observation fails its link closed (never verified).
        /// </summary>
        public static Result Evaluate(
            AccountIdentityObservation accountIdentity,
            TargetAccountConnectionObservation connectionAssociation,
            EnvironmentClass environment)
        {
            var result = new Result();

            result.AccountVerified =
                AccountIdentityVerifier.Evaluate(accountIdentity).Status == DiagnosticStatus.PASS;
            result.ConnectionVerified =
                TargetAccountConnectionVerifier.Evaluate(connectionAssociation).Status == DiagnosticStatus.PASS;
            result.ProviderVerified =
                accountIdentity != null
                && connectionAssociation != null
                && accountIdentity.AccountProviderMatch == ProviderIdentityMatch.Matches
                && connectionAssociation.ConnectionProviderMatch == ProviderIdentityMatch.Matches;
            result.EnvironmentEstablished = environment != EnvironmentClass.Unknown;

            if (!result.AccountVerified)
            {
                result.ReasonCodes.Add(AccountUnverified);
            }

            if (!result.ConnectionVerified)
            {
                result.ReasonCodes.Add(ConnectionUnverified);
            }

            if (!result.ProviderVerified)
            {
                result.ReasonCodes.Add(ProviderUnverified);
            }

            if (!result.EnvironmentEstablished)
            {
                result.ReasonCodes.Add(EnvironmentUnknown);
            }

            if (result.FullyVerified)
            {
                result.ReasonCodes.Add(Verified);
            }

            return result;
        }
    }
}
