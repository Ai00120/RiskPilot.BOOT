using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

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
            new Regex(@"(?i)\b(pass(word|phrase)?|secret|token|api[_-]?key|apikey|access[_-]?key|private[_-]?key|bearer|authorization|auth[_-]?token|session[_-]?key|client[_-]?secret)\b\s*[:=]\s*(""[^""]*""|'[^']*'|[^\s,;""']+)", RegexOptions.Compiled),
            // Authorization: Bearer <token>
            new Regex(@"(?i)\bbearer\s+[A-Za-z0-9\-\._~\+/]+=*", RegexOptions.Compiled),
            // Account numbers / long digit runs (broker account ids).
            new Regex(@"\b\d{6,}\b", RegexOptions.Compiled),
            // Email addresses.
            new Regex(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}", RegexOptions.Compiled),
            // Windows user-profile paths reveal the operator's user name.
            new Regex(@"(?i)([A-Za-z]:\\Users\\)[^\\\s]+", RegexOptions.Compiled),
            // IPv4 addresses.
            new Regex(@"\b(?:\d{1,3}\.){3}\d{1,3}\b", RegexOptions.Compiled),
        };

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

            return result;
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
