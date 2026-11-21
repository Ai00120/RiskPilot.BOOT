using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace RiskPilot.Diagnostics.Nt8.Core.Identity
{
    /// <summary>
    /// How a raw NinjaTrader provider value was classified. The classification is what lets the
    /// diagnostic distinguish "a real provider whose name differs" (a definite contradiction) from
    /// "a generic slot that carries no vendor identity" (unproven, fail closed).
    /// </summary>
    public enum ProviderIdentityKind
    {
        /// <summary>The value could not be read (null or empty).</summary>
        Unobservable = 0,

        /// <summary>
        /// The value is a generic placeholder slot NinjaTrader assigns to a third-party adapter
        /// that has no dedicated enum member (<c>Provider1..Provider40</c>, <c>Custom0..Custom45</c>,
        /// <c>Unknown</c>, or an undefined numeric value). A placeholder slot's name is an ordinal,
        /// not a vendor: which adapter occupies the slot depends on the installed host, so it can
        /// never be assumed to be any particular provider.
        /// </summary>
        PlaceholderSlot = 1,

        /// <summary>
        /// The value is a real, named provider enum member (for example <c>Simulator</c>, <c>CQG</c>)
        /// or an unrecognized free-form string. It carries a name that can be compared directly.
        /// </summary>
        Named = 2
    }

    /// <summary>
    /// Three-valued result of comparing an observed provider value with the required provider. A
    /// Boolean cannot express "the host reported a value but it carries no vendor identity", which
    /// is exactly the <c>Provider14</c> case; that case must fail closed rather than become a false
    /// mismatch.
    /// </summary>
    public enum ProviderIdentityMatch
    {
        /// <summary>A verified named member (or owner-authorized slot mapping) equals the required provider.</summary>
        Matches = 0,

        /// <summary>
        /// A verified named member (or owner-authorized slot mapping) is a different provider. This
        /// is a definite contradiction and outranks an unobservable field.
        /// </summary>
        Differs = 1,

        /// <summary>
        /// The observed value is a generic placeholder slot (or unreadable) and no verified mapping
        /// resolves it. The provider identity is unproven: fail closed, never a false PASS and never
        /// a false FAIL.
        /// </summary>
        Unresolved = 2
    }

    /// <summary>
    /// Host-free provider identity contract.
    /// <para>
    /// Verified against the installed NinjaTrader 8 host: <c>NinjaTrader.Cbi.Provider</c> has
    /// <b>no</b> dedicated <c>Rithmic</c> member. Its named members are
    /// <c>Collective2=0 .. Yahoo=19</c>; every third-party adapter without a dedicated member is
    /// assigned a generic placeholder slot (<c>Provider1..Provider40</c> = 20..59,
    /// <c>Custom0..Custom45</c> = 1000..1045, <c>Unknown</c> = 9999). The diagnostic environment
    /// therefore reports <c>Provider14</c> (= 33), a placeholder slot that carries no vendor
    /// identity.
    /// </para>
    /// <para>
    /// Comparing that placeholder to the literal <c>"Rithmic"</c> is both brittle and wrong: it
    /// produced the reported account/provider and connection/provider "mismatches" and the failed
    /// association and agreement checks. This type classifies the raw value and compares it with the
    /// required provider three-valued, so a placeholder stays <see cref="ProviderIdentityMatch.Unresolved"/>
    /// (INCONCLUSIVE) instead of a false FAIL.
    /// </para>
    /// <para>
    /// A placeholder slot may only be accepted through an explicit, verified owner-authorized
    /// slot-&gt;vendor mapping (the same owner-controlled pattern as D-078). D-097 authorizes exactly
    /// one such mapping for the one designated environment: <c>Provider14 -&gt; Rithmic</c>, compiled
    /// immutably in <see cref="OwnerAuthorizedSlots"/>. No other slot is mapped and none is inferred
    /// from a display name or an online reference; every other placeholder stays
    /// <see cref="ProviderIdentityMatch.Unresolved"/> (fail closed).
    /// </para>
    /// </summary>
    public static class ProviderIdentity
    {
        /// <summary>The required provider name for the permanent RiskPilot.BOT identity.</summary>
        public const string ExpectedProvider = "Rithmic";

        /// <summary>The observed NinjaTrader placeholder slot that the Owner authorized as Rithmic (D-097).</summary>
        public const string OwnerAuthorizedRithmicSlot = "Provider14";

        /// <summary>An empty owner-authorized slot mapping (the fail-closed default).</summary>
        public static readonly IReadOnlyDictionary<string, string> NoOwnerAuthorizedSlots =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// The compiled, immutable Owner-authorized placeholder-slot -&gt; vendor mapping (D-097).
        /// <para>
        /// D-097 explicitly authorizes <c>Provider14 -&gt; Rithmic</c> for the one designated
        /// NinjaTrader/Rithmic environment. The mapping is BOOT's own compiled constant: it is never
        /// read from configuration, environment or the host, and the exposed dictionary is a
        /// read-only view that cannot be mutated at runtime. Keys are matched exactly (ordinal), so
        /// a differently-cased or different slot never resolves. No other slot is mapped; every
        /// other placeholder stays <see cref="ProviderIdentityMatch.Unresolved"/> (fail closed).
        /// </para>
        /// </summary>
        public static readonly IReadOnlyDictionary<string, string> OwnerAuthorizedSlots =
            new ReadOnlyDictionary<string, string>(
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    { OwnerAuthorizedRithmicSlot, ExpectedProvider }
                });

        /// <summary>
        /// Classifies a raw provider value. A null/empty value is unobservable; a generic slot name
        /// (or an undefined numeric enum value) is a placeholder; anything else is a named value.
        /// </summary>
        public static ProviderIdentityKind Classify(string raw)
        {
            var value = Normalize(raw);
            if (string.IsNullOrEmpty(value))
            {
                return ProviderIdentityKind.Unobservable;
            }

            return IsPlaceholderSlot(value) ? ProviderIdentityKind.PlaceholderSlot : ProviderIdentityKind.Named;
        }

        /// <summary>
        /// True when the value is a generic placeholder slot: <c>ProviderN</c>, <c>CustomN</c>,
        /// <c>Unknown</c>, or an undefined numeric enum value (which NinjaTrader renders as its
        /// number when no member name exists). Such a value carries no vendor identity.
        /// </summary>
        public static bool IsPlaceholderSlot(string raw)
        {
            var value = Normalize(raw);
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            if (IsDigits(value))
            {
                // An undefined enum value is rendered as its number, so it is just as anonymous as
                // a ProviderN slot.
                return true;
            }

            if (string.Equals(value, "Unknown", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return HasNumericSuffix(value, "Provider") || HasNumericSuffix(value, "Custom");
        }

        /// <summary>
        /// Resolves a <b>placeholder slot</b> to a verified vendor through the owner-authorized
        /// mapping, or returns null when no verified mapping applies. The mapping is matched
        /// case-insensitively on the exact slot name.
        /// <para>
        /// The mapping applies to placeholder slots only: an explicitly named provider (for example
        /// <c>Yahoo</c> or <c>Simulator</c>) already carries a real vendor identity, so it is never
        /// remapped, even when the mapping dictionary contains an entry for its name. Such a value
        /// resolves to null and is then compared by its own name.
        /// </para>
        /// </summary>
        public static string Resolve(string raw, IReadOnlyDictionary<string, string> ownerAuthorizedSlots)
        {
            var value = Normalize(raw);
            if (string.IsNullOrEmpty(value)
                || ownerAuthorizedSlots == null
                || !IsPlaceholderSlot(value))
            {
                return null;
            }

            string vendor;
            if (ownerAuthorizedSlots.TryGetValue(value, out vendor) && !string.IsNullOrWhiteSpace(vendor))
            {
                return vendor.Trim();
            }

            return null;
        }

        /// <summary>
        /// Compares an observed provider value with the required provider, three-valued.
        /// <list type="bullet">
        /// <item>an unreadable value is <see cref="ProviderIdentityMatch.Unresolved"/>;</item>
        /// <item>a placeholder slot with no verified owner-authorized mapping is
        /// <see cref="ProviderIdentityMatch.Unresolved"/> (never a false mismatch);</item>
        /// <item>a placeholder slot with a verified mapping is compared by its mapped vendor;</item>
        /// <item>a named value is compared by name (case-insensitive).</item>
        /// </list>
        /// </summary>
        public static ProviderIdentityMatch Compare(
            string observed,
            string required,
            IReadOnlyDictionary<string, string> ownerAuthorizedSlots)
        {
            var value = Normalize(observed);
            if (string.IsNullOrEmpty(value) || string.IsNullOrWhiteSpace(required))
            {
                return ProviderIdentityMatch.Unresolved;
            }

            var mapped = Resolve(value, ownerAuthorizedSlots);
            if (!string.IsNullOrEmpty(mapped))
            {
                return string.Equals(mapped, required.Trim(), StringComparison.OrdinalIgnoreCase)
                    ? ProviderIdentityMatch.Matches
                    : ProviderIdentityMatch.Differs;
            }

            if (IsPlaceholderSlot(value))
            {
                // A generic slot with no verified mapping carries no identity: unproven, not wrong.
                return ProviderIdentityMatch.Unresolved;
            }

            return string.Equals(value, required.Trim(), StringComparison.OrdinalIgnoreCase)
                ? ProviderIdentityMatch.Matches
                : ProviderIdentityMatch.Differs;
        }

        /// <summary>
        /// True when the observed value equals the expected provider under the compiled
        /// Owner-authorized mapping (<see cref="OwnerAuthorizedSlots"/>). The D-097 slot
        /// <c>Provider14</c> therefore resolves to the expected provider; any other placeholder slot
        /// returns false because it is not mapped and is never assumed to be the expected provider.
        /// </summary>
        public static bool IsExpectedProvider(string observed)
        {
            return Compare(observed, ExpectedProvider, OwnerAuthorizedSlots) == ProviderIdentityMatch.Matches;
        }

        /// <summary>
        /// Compares two observed provider values with each other (an agreement check, not against a
        /// required value). Returns false — leaving <paramref name="agree"/> unspecified — when
        /// either side is unreadable or a placeholder slot without a verified owner-authorized
        /// mapping, so the agreement is unproven rather than a false mismatch.
        /// </summary>
        public static bool TryAgree(
            string first,
            string second,
            IReadOnlyDictionary<string, string> ownerAuthorizedSlots,
            out bool agree)
        {
            agree = false;

            var left = ResolveForComparison(first, ownerAuthorizedSlots);
            var right = ResolveForComparison(second, ownerAuthorizedSlots);
            if (left == null || right == null)
            {
                return false;
            }

            agree = string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
            return true;
        }

        private static string ResolveForComparison(string raw, IReadOnlyDictionary<string, string> ownerAuthorizedSlots)
        {
            var value = Normalize(raw);
            if (string.IsNullOrEmpty(value))
            {
                return null;
            }

            var mapped = Resolve(value, ownerAuthorizedSlots);
            if (!string.IsNullOrEmpty(mapped))
            {
                return mapped;
            }

            return IsPlaceholderSlot(value) ? null : value;
        }

        /// <summary>
        /// True when the observed value is a generic placeholder slot (or unreadable) that the
        /// compiled Owner-authorized mapping cannot resolve. The D-097 <c>Provider14</c> slot is
        /// resolved and therefore not unresolved; any other slot is. The caller reports this as
        /// unproven (INCONCLUSIVE).
        /// </summary>
        public static bool IsUnresolved(string observed)
        {
            return Compare(observed, ExpectedProvider, OwnerAuthorizedSlots) == ProviderIdentityMatch.Unresolved;
        }

        private static string Normalize(string raw)
        {
            return raw == null ? null : raw.Trim();
        }

        private static bool IsDigits(string value)
        {
            for (var i = 0; i < value.Length; i++)
            {
                if (value[i] < '0' || value[i] > '9')
                {
                    return false;
                }
            }

            return value.Length > 0;
        }

        private static bool HasNumericSuffix(string value, string prefix)
        {
            if (value.Length <= prefix.Length
                || !value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            for (var i = prefix.Length; i < value.Length; i++)
            {
                if (value[i] < '0' || value[i] > '9')
                {
                    return false;
                }
            }

            return true;
        }
    }
}
