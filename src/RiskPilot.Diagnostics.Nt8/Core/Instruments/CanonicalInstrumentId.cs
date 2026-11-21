using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace RiskPilot.Diagnostics.Nt8.Core.Instruments
{
    /// <summary>
    /// Derives the canonical instrument id from a host contract symbol, independently of
    /// RiskPilot.BOT. The parse is strict: an unparseable symbol yields null rather than a
    /// best-effort guess, so an unknown contract is never silently mapped to a known one.
    /// <para>
    /// Format: <c>ROOT_FUTURES_&lt;EXCHANGE&gt;_&lt;MON&gt;&lt;YY&gt;</c>, for example
    /// <c>ES 12-26</c> on CME → <c>ES_FUTURES_CME_DEC26</c>. This mirrors the identity shape
    /// the trading repository uses so the two speak the same language without a code
    /// dependency.
    /// </para>
    /// </summary>
    public static class CanonicalInstrumentId
    {
        /// <summary>The recorded evidence contract identity (ES December 2026 on CME).</summary>
        public const string EvidenceIdentity = "ES_FUTURES_CME_DEC26";

        private static readonly string[] MonthAbbreviations =
        {
            "JAN", "FEB", "MAR", "APR", "MAY", "JUN",
            "JUL", "AUG", "SEP", "OCT", "NOV", "DEC"
        };

        private static readonly Regex FuturesSymbol =
            new Regex(@"^(?<root>[A-Za-z]{1,6})\s*[-\s]\s*(?<month>\d{1,2})\s*-\s*(?<year>\d{2,4})$", RegexOptions.Compiled);

        private static readonly Regex FuturesSymbolWithMonthName =
            new Regex(@"^(?<root>[A-Za-z]{1,6})\s*[-\s]\s*(?<month>[A-Za-z]{3})\s*[-\s]?\s*(?<year>\d{2,4})$", RegexOptions.Compiled);

        /// <summary>
        /// Attempts to parse a host contract symbol into its root, month and full year.
        /// Accepts both the numeric month form (<c>ES 12-26</c>) and the month-name form the host
        /// may render (<c>ES DEC26</c>), so the same contract is never treated as a different one
        /// merely because of its display notation. Returns false when the symbol is not a
        /// recognised futures contract.
        /// </summary>
        public static bool TryParse(string symbol, out string root, out int month, out int fullYear)
        {
            root = null;
            month = 0;
            fullYear = 0;
            if (string.IsNullOrEmpty(symbol))
            {
                return false;
            }

            var trimmed = symbol.Trim();
            var match = FuturesSymbol.Match(trimmed);
            if (!match.Success)
            {
                match = FuturesSymbolWithMonthName.Match(trimmed);
                if (!match.Success)
                {
                    return false;
                }

                month = Array.IndexOf(MonthAbbreviations, match.Groups["month"].Value.ToUpperInvariant()) + 1;
                if (month == 0)
                {
                    return false;
                }
            }
            else if (!int.TryParse(match.Groups["month"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out month)
                || month < 1 || month > 12)
            {
                return false;
            }

            if (!int.TryParse(match.Groups["year"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedYear))
            {
                return false;
            }

            root = match.Groups["root"].Value.ToUpperInvariant();
            fullYear = parsedYear >= 100 ? parsedYear : 2000 + parsedYear;
            return true;
        }

        /// <summary>
        /// Builds the canonical id from a parsed symbol and a mapped exchange name. Returns
        /// null when the symbol cannot be parsed.
        /// </summary>
        public static string Build(string symbol, string mappedExchange)
        {
            if (!TryParse(symbol, out var root, out var month, out var year) || string.IsNullOrEmpty(mappedExchange))
            {
                return null;
            }

            var twoDigitYear = year % 100;
            return root + "_FUTURES_" + mappedExchange + "_"
                + MonthAbbreviations[month - 1]
                + twoDigitYear.ToString("D2", CultureInfo.InvariantCulture);
        }
    }
}
