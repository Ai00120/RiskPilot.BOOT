using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using RiskPilot.Diagnostics.Nt8.Core.Identity;

namespace RiskPilot.Diagnostics.Nt8.Core.Security
{
    /// <summary>
    /// Removes or masks sensitive values before they can reach a log line, a finding,
    /// or a report file. This is a defensive, fail-closed filter: when a value looks
    /// like a credential, an account number, or a personal path, it is masked.
    /// </summary>
    public static class SecretRedactor
    {
        public const string Mask = "***REDACTED***";

        private static readonly Regex[] MaskPatterns =
        {
            // key=value / "key": "value" style secrets.
            new Regex(@"(?i)\b(pass(word|phrase)?|passwd|passcode|pwd|pin|otp|secret|credential|token|api[_-]?key|apikey|access[_-]?key|private[_-]?key|bearer|authorization|auth[_-]?token|session[_-]?key|client[_-]?secret)\b\s*[:=]\s*(""[^""]*""|'[^']*'|[^\s,;""']+)", RegexOptions.Compiled),
            // Authorization: Bearer <token>
            new Regex(@"(?i)\bbearer\s+[A-Za-z0-9\-\._~\+/]+=*", RegexOptions.Compiled),
            // Email addresses.
            new Regex(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}", RegexOptions.Compiled),
            // Windows user-profile paths reveal the operator's user name.
            new Regex(@"(?i)([A-Za-z]:\\Users\\)[^\\\s]+", RegexOptions.Compiled),
        };

        // IPv4 addresses can reveal network topology, so a dotted quad is masked. The rule is
        // applied separately from the pattern list because the tool's own declared version
        // constants (for example the NinjaTrader target "8.1.9.0") have the same shape but are
        // not secrets; masking them would corrupt the compatibility report the tool is required
        // to publish. Only the exact declared version strings are preserved -- a genuine IP
        // address is still masked.
        private static readonly Regex Ipv4 = new Regex(@"\b(?:\d{1,3}\.){3}\d{1,3}\b", RegexOptions.Compiled);

        // Exact non-secret version strings that the tool publishes as evidence. Anything else of
        // the dotted-quad shape is treated as a possible IP address and masked.
        private static readonly string[] KnownNonSecretVersions = BuildKnownNonSecretVersions();

        private static string[] BuildKnownNonSecretVersions()
        {
            return new[]
            {
                NinjaTraderCompatibility.TargetVersion,
                NinjaTraderCompatibility.MinimumSupportedVersion,
                BuildIdentity.ToolVersion,
                BuildIdentity.AssemblyVersionString
            };
        }

        // Account numbers / long digit runs (broker account ids). This rule is applied
        // separately from the pattern list because a bare 6+ digit run is only a *possible*
        // account number: the same shape is produced by legitimate diagnostic counters
        // (for example ALL_CANDIDATES_INVALID=311179 or an epoch). Masking those would
        // corrupt real evidence and break traceability, so a run that is the value of a
        // structured key=value / key:value field is left intact. A free-text run
        // ("acct 12345678") is still masked.
        private static readonly Regex LongDigitRun = new Regex(@"\b\d{6,}\b", RegexOptions.Compiled);

        /// <summary>Masks every sensitive token found in <paramref name="value"/>.</summary>
        public static string Redact(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return value;
            }

            var result = value;
            foreach (var pattern in MaskPatterns)
            {
                result = pattern.Replace(result, Mask);
            }

            result = MaskIpv4(result);
            return MaskAccountDigitRuns(result);
        }

        /// <summary>
        /// Masks dotted-quad IPv4 addresses but preserves the tool's own declared version
        /// constants (for example the NinjaTrader target "8.1.9.0"), which share the shape but are
        /// published evidence rather than secrets. Only an exact known version is preserved.
        /// </summary>
        private static string MaskIpv4(string value)
        {
            return Ipv4.Replace(value, match =>
            {
                foreach (var known in KnownNonSecretVersions)
                {
                    if (string.Equals(match.Value, known, StringComparison.Ordinal))
                    {
                        return match.Value;
                    }
                }

                return Mask;
            });
        }

        /// <summary>
        /// Masks a bare 6+ digit run (a possible account number) but preserves a run that is
        /// the value of a structured key=value / key:value field, because that shape is how the
        /// tool reports its own counters (for example <c>ALL_CANDIDATES_INVALID=311179</c>) and
        /// masking them would corrupt real evidence. A run whose key looks like an account field
        /// is masked regardless, so an <c>account=12345678</c> form is still protected.
        /// </summary>
        private static string MaskAccountDigitRuns(string value)
        {
            return LongDigitRun.Replace(value, match =>
            {
                var index = match.Index - 1;
                while (index >= 0 && char.IsWhiteSpace(value[index]))
                {
                    index--;
                }

                if (index < 0 || (value[index] != '=' && value[index] != ':'))
                {
                    return Mask;
                }

                var keyEnd = index - 1;
                while (keyEnd >= 0 && char.IsWhiteSpace(value[keyEnd]))
                {
                    keyEnd--;
                }

                // Skip an optional closing quote so a JSON key ("accountRef":12345678) is seen.
                if (keyEnd >= 0 && (value[keyEnd] == '"' || value[keyEnd] == '\''))
                {
                    keyEnd--;
                }

                var keyStart = keyEnd;
                while (keyStart >= 0 && (char.IsLetterOrDigit(value[keyStart]) || value[keyStart] == '_' || value[keyStart] == '-' || value[keyStart] == '.'))
                {
                    keyStart--;
                }

                var key = keyEnd >= keyStart + 1 ? value.Substring(keyStart + 1, keyEnd - keyStart) : string.Empty;
                if (key.IndexOf("account", StringComparison.OrdinalIgnoreCase) >= 0
                    || key.IndexOf("acct", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return Mask;
                }

                return match.Value;
            });
        }

        /// <summary>
        /// Redacts a single scalar field that is expected to hold exactly one value.
        /// A field that becomes fully masked is reported as <see cref="Mask"/> rather
        /// than an empty string so that the consumer can tell "hidden" from "absent".
        /// </summary>
        public static string RedactField(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return value;
            }

            return Redact(value);
        }

        public static IReadOnlyList<string> RedactAll(IEnumerable<string> values)
        {
            var list = new List<string>();
            if (values == null)
            {
                return list;
            }

            foreach (var value in values)
            {
                list.Add(Redact(value));
            }

            return list;
        }

        /// <summary>Redacts a string array, returning a new array (never null).</summary>
        public static string[] RedactArray(string[] values)
        {
            if (values == null)
            {
                return new string[0];
            }

            var result = new string[values.Length];
            for (var i = 0; i < values.Length; i++)
            {
                result[i] = Redact(values[i]);
            }

            return result;
        }

        /// <summary>True when the text still appears to contain a credential-like token.</summary>
        public static bool ContainsPotentialSecret(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            return Redact(value) != value;
        }

        public static string DescribePatterns()
        {
            var builder = new StringBuilder();
            builder.Append("credential-keywords, bearer-tokens, account-digits, emails, user-paths, ipv4");
            return builder.ToString();
        }
    }
}
