using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using RiskPilot.Diagnostics.Nt8.Core.Contract;
using RiskPilot.Diagnostics.Nt8.Core.Identity;
using RiskPilot.Diagnostics.Nt8.Core.Models.Observations;

namespace RiskPilot.Diagnostics.Nt8.Core.Security
{
    /// <summary>
    /// The read-only security scan, owned by M8. It reads production source through an
    /// <see cref="ISecurityScanSource"/> and applies the rule catalogue: no order/account mutation
    /// API, no live-trading enable path, no reflection/unsafe/interop, the Owner identity literals
    /// confined to the reviewed definition file, and the AddOn's trading-disabled declaration. It
    /// preserves every check the offline <c>verification/addon-smoke</c> scan performed.
    /// <para>
    /// The scan is strictly read-only. It reads source text and never writes, and it never
    /// touches, mutates or reloads any NinjaTrader configuration or host state. It never throws:
    /// an unreadable source, a malformed rule or an empty file set produces an UNOBSERVABLE
    /// observation, never a PASS.
    /// </para>
    /// </summary>
    public static class SecurityScanner
    {
        private static readonly string[] DefinitionFileSuffixes =
        {
            "Core/Identity/AccountIdentityVerifier.cs",
            "Identity/AccountIdentityVerifier.cs",
            "AccountIdentityVerifier.cs"
        };

        private static readonly string[] AddOnFileSuffixes =
        {
            "Host/DiagnosticAddOn.cs",
            "AddOns/DiagnosticAddOn.cs",
            "DiagnosticAddOn.cs"
        };

        private static readonly string[] ReflectionAllowedSuffixes =
        {
            "Properties/AssemblyInfo.cs",
            "AssemblyInfo.cs"
        };

        /// <summary>
        /// Runs the scan. It never throws. When the source is unavailable, or no file can be read,
        /// the result is <c>Executed=false</c> so M8 reports INCONCLUSIVE (fail-closed).
        /// </summary>
        public static SecurityScanObservation Scan(ISecurityScanSource source)
        {
            var observation = new SecurityScanObservation
            {
                RuleCount = SecurityScanCatalog.RuleCount
            };

            if (source == null)
            {
                return Unavailable(observation, SecurityScanReasonCodes.SourceUnavailable);
            }

            bool available;
            try
            {
                available = source.IsAvailable;
            }
            catch (Exception)
            {
                available = false;
            }

            if (!available)
            {
                return Unavailable(observation, SecurityScanReasonCodes.SourceUnavailable);
            }

            IReadOnlyList<string> files;
            try
            {
                files = source.GetSourceFiles();
            }
            catch (Exception)
            {
                return Unavailable(observation, SecurityScanReasonCodes.Exception);
            }

            if (files == null || files.Count == 0)
            {
                return Unavailable(observation, SecurityScanReasonCodes.NoSourceFiles);
            }

            // Read every candidate file first, then resolve the special files by suffix so both
            // the standalone repo layout and the NinjaScript custom-compile layout are handled.
            var texts = new Dictionary<string, string>(StringComparer.Ordinal);
            var filesScanned = 0;
            var unreadable = 0;
            foreach (var relativePath in files)
            {
                if (string.IsNullOrEmpty(relativePath))
                {
                    continue;
                }

                var rel = NormalizePath(relativePath);
                string text;
                try
                {
                    text = source.ReadFile(relativePath);
                }
                catch (Exception)
                {
                    text = null;
                }

                if (text == null)
                {
                    unreadable++;
                    continue;
                }

                texts[rel] = text;
                filesScanned++;
            }

            if (filesScanned == 0)
            {
                return Unavailable(observation, SecurityScanReasonCodes.NoSourceFiles);
            }

            var definitionPath = ResolveBySuffix(texts.Keys, DefinitionFileSuffixes);
            var addOnPath = ResolveBySuffix(texts.Keys, AddOnFileSuffixes);
            var reflectionAllowedPath = ResolveBySuffix(texts.Keys, ReflectionAllowedSuffixes);

            // ---- forbidden-pattern rules + identity-literal confinement
            var identityLeak = false;
            foreach (var pair in texts)
            {
                var stripped = StripCommentsAndStrings(pair.Value);
                ApplyRules(observation, pair.Key, stripped, reflectionAllowedPath);

                if (!string.Equals(pair.Key, definitionPath, StringComparison.Ordinal)
                    && ContainsIdentityLiteral(pair.Value))
                {
                    observation.Hits.Add(new SecurityScanFinding
                    {
                        RuleId = "IDENTITY_LITERAL",
                        Category = SecurityScanCategories.IdentityLeak,
                        File = pair.Key,
                        Line = LineNumber(pair.Value, FirstLiteralIndex(pair.Value))
                    });
                    identityLeak = true;
                }
            }

            if (identityLeak)
            {
                observation.ReasonCodes.Add(SecurityScanReasonCodes.IdentityLeak);
            }

            if (observation.HitCount > 0)
            {
                observation.ReasonCodes.Add(SecurityScanReasonCodes.ForbiddenApi);
            }

            // ---- trading-disabled invariant (observable only when the AddOn file was read)
            observation.TradingDisabledObservable = addOnPath != null;
            observation.TradingDisabledDeclared = addOnPath != null
                && texts[addOnPath].IndexOf(SecurityScanCatalog.TradingDisabledToken, StringComparison.Ordinal) >= 0;
            if (observation.TradingDisabledObservable && !observation.TradingDisabledDeclared)
            {
                observation.ReasonCodes.Add(SecurityScanReasonCodes.TradingDisabledMissing);
            }

            // ---- identity-definition file present and actually defining the identity literals
            observation.IdentityDefinitionPresent = definitionPath != null;
            observation.IdentityDefinitionReviewed = definitionPath != null
                && ContainsIdentityLiteral(texts[definitionPath]);
            if (!observation.IdentityDefinitionPresent)
            {
                observation.ReasonCodes.Add(SecurityScanReasonCodes.DefinitionMissing);
            }
            else if (!observation.IdentityDefinitionReviewed)
            {
                observation.ReasonCodes.Add(SecurityScanReasonCodes.DefinitionLiteralMissing);
            }

            if (unreadable > 0)
            {
                // A file in the set could not be read: the scan did not cover everything it should.
                // This is recorded (never silently ignored) and keeps the scan from claiming a clean
                // pass on an incompletely-covered source set.
                observation.ReasonCodes.Add(SecurityScanReasonCodes.Exception);
            }

            observation.Executed = true;
            observation.SourceAvailable = true;
            observation.FilesScanned = filesScanned;

            if (observation.HitCount == 0
                && observation.TradingDisabledObservable
                && observation.TradingDisabledDeclared
                && observation.IdentityDefinitionReviewed
                && unreadable == 0)
            {
                observation.ReasonCodes.Add(SecurityScanReasonCodes.Passed);
            }

            return observation;
        }

        /// <summary>
        /// True when <paramref name="text"/> contains any Owner identity literal. The literals are
        /// never hardcoded here: they are drawn from <see cref="ExpectedAccountIdentity"/>, so this
        /// scanner can never itself become an identity-leak site.
        /// </summary>
        public static bool ContainsIdentityLiteral(string text)
        {
            return FirstLiteralIndex(text) >= 0;
        }

        private static int FirstLiteralIndex(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return -1;
            }

            var account = text.IndexOf(ExpectedAccountIdentity.AccountName, StringComparison.Ordinal);
            var connection = text.IndexOf(ExpectedAccountIdentity.ConnectionName, StringComparison.Ordinal);
            if (account < 0)
            {
                return connection;
            }

            if (connection < 0)
            {
                return account;
            }

            return Math.Min(account, connection);
        }

        private static void ApplyRules(
            SecurityScanObservation observation,
            string relativePath,
            string stripped,
            string reflectionAllowedPath)
        {
            var isReflectionAllowedFile = reflectionAllowedPath != null
                && string.Equals(relativePath, reflectionAllowedPath, StringComparison.Ordinal);

            foreach (var rule in SecurityScanCatalog.Rules)
            {
                // AssemblyInfo.cs is allowed to reference System.Reflection for assembly-level
                // attributes only; every other reflection rule still applies to it.
                if (rule.Id == "SYSTEM_REFLECTION" && isReflectionAllowedFile)
                {
                    continue;
                }

                var options = rule.CaseInsensitive
                    ? RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
                    : RegexOptions.CultureInvariant;

                Match match;
                try
                {
                    match = Regex.Match(stripped, rule.Pattern, options);
                }
                catch (Exception)
                {
                    // A malformed rule pattern is itself a scan defect: record it, never pass on it.
                    if (!observation.ReasonCodes.Contains(SecurityScanReasonCodes.Exception))
                    {
                        observation.ReasonCodes.Add(SecurityScanReasonCodes.Exception);
                    }

                    continue;
                }

                if (!match.Success)
                {
                    continue;
                }

                observation.Hits.Add(new SecurityScanFinding
                {
                    RuleId = rule.Id,
                    Category = rule.Category,
                    File = relativePath,
                    Line = LineNumber(stripped, match.Index)
                });
            }
        }

        private static string ResolveBySuffix(ICollection<string> paths, string[] suffixes)
        {
            string best = null;
            foreach (var path in paths)
            {
                var normalized = NormalizePath(path);
                foreach (var suffix in suffixes)
                {
                    if (normalized.EndsWith(suffix, StringComparison.Ordinal)
                        && (best == null || normalized.Length < best.Length))
                    {
                        best = normalized;
                    }
                }
            }

            return best;
        }

        private static SecurityScanObservation Unavailable(SecurityScanObservation observation, string reason)
        {
            observation.UnavailableReason = reason;
            observation.ReasonCodes.Add(reason);
            return observation;
        }

        private static int LineNumber(string text, int index)
        {
            if (text == null || index <= 0)
            {
                return 1;
            }

            var line = 1;
            var limit = Math.Min(index, text.Length);
            for (var i = 0; i < limit; i++)
            {
                if (text[i] == '\n')
                {
                    line++;
                }
            }

            return line;
        }

        private static string NormalizePath(string path)
        {
            return (path ?? string.Empty).Replace('\\', '/');
        }

        /// <summary>
        /// Removes comments and string literals before a rule is applied, so a forbidden keyword
        /// appearing only inside a comment or a string is never a false positive. Mirrors the
        /// offline scan exactly.
        /// </summary>
        public static string StripCommentsAndStrings(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text ?? string.Empty;
            }

            var result = Regex.Replace(text, "/\\*.*?\\*/", " ", RegexOptions.Singleline);
            result = Regex.Replace(result, "//[^\\n]*", " ");
            result = Regex.Replace(result, "@\"(?:[^\"]|\"\")*\"", "\"\"", RegexOptions.Singleline);
            result = Regex.Replace(result, "\"(?:\\\\.|[^\"\\\\])*\"", "\"\"");
            return result;
        }
    }
}
