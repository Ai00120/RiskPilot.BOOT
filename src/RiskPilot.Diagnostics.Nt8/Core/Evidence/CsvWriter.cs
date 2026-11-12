using System.Collections.Generic;
using System.Globalization;
using System.Text;
using RiskPilot.Diagnostics.Nt8.Core.Security;

namespace RiskPilot.Diagnostics.Nt8.Core.Evidence
{
    /// <summary>
    /// Minimal RFC-4180 CSV writer. Every cell is passed through <see cref="SecretRedactor"/>
    /// before it is written, so a CSV artifact can never carry a credential or an account
    /// number even if a caller forgot to redact. Numbers are formatted with the invariant
    /// culture so the file is stable across hosts.
    /// </summary>
    public sealed class CsvWriter
    {
        private readonly StringBuilder _builder = new StringBuilder();
        private readonly List<string> _columns;

        public CsvWriter(IReadOnlyList<string> columns)
        {
            _columns = new List<string>();
            if (columns != null)
            {
                foreach (var column in columns)
                {
                    _columns.Add(column ?? string.Empty);
                }
            }

            WriteRow(_columns);
        }

        /// <summary>Writes one data row. Short rows are padded; long rows are truncated.</summary>
        public void WriteRow(IReadOnlyList<string> cells)
        {
            for (var i = 0; i < _columns.Count; i++)
            {
                if (i > 0)
                {
                    _builder.Append(',');
                }

                var value = cells != null && i < cells.Count ? cells[i] : string.Empty;
                _builder.Append(Escape(SecretRedactor.Redact(value ?? string.Empty)));
            }

            _builder.Append("\r\n");
        }

        /// <summary>Convenience overload for a fixed set of cells.</summary>
        public void WriteRow(params string[] cells)
        {
            WriteRow((IReadOnlyList<string>)cells);
        }

        public static string Invariant(double value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        public static string Invariant(long value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        public static string Invariant(int value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        private static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            var needsQuotes = value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0;
            if (!needsQuotes)
            {
                return value;
            }

            var builder = new StringBuilder(value.Length + 2);
            builder.Append('"');
            foreach (var c in value)
            {
                if (c == '"')
                {
                    builder.Append("\"\"");
                }
                else
                {
                    builder.Append(c);
                }
            }

            builder.Append('"');
            return builder.ToString();
        }

        public override string ToString()
        {
            return _builder.ToString();
        }
    }
}
