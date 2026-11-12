using System.Collections.Generic;
using System.Text;
using RiskPilot.Diagnostics.Nt8.Core.Security;

namespace RiskPilot.Diagnostics.Nt8.Core.Evidence
{
    /// <summary>
    /// Minimal, dependency-free HTML builder. Every text node and attribute value passes
    /// through <see cref="SecretRedactor"/> and is HTML-escaped, so a rendered report can
    /// never inject markup or leak a credential. The output is deterministic.
    /// </summary>
    public sealed class HtmlWriter
    {
        private readonly StringBuilder _builder = new StringBuilder();
        private int _indent;

        public HtmlWriter WriteRaw(string html)
        {
            _builder.Append(html);
            return this;
        }

        public HtmlWriter Open(string tag, string cssClass = null)
        {
            Indent();
            _builder.Append('<').Append(tag);
            if (!string.IsNullOrEmpty(cssClass))
            {
                _builder.Append(" class=\"").Append(Escape(cssClass)).Append('"');
            }

            _builder.Append(">\n");
            _indent++;
            return this;
        }

        public HtmlWriter Close(string tag)
        {
            _indent = _indent > 0 ? _indent - 1 : 0;
            Indent();
            _builder.Append("</").Append(tag).Append(">\n");
            return this;
        }

        public HtmlWriter Element(string tag, string text, string cssClass = null)
        {
            Indent();
            _builder.Append('<').Append(tag);
            if (!string.IsNullOrEmpty(cssClass))
            {
                _builder.Append(" class=\"").Append(Escape(cssClass)).Append('"');
            }

            _builder.Append('>').Append(Escape(SecretRedactor.Redact(text ?? string.Empty))).Append("</").Append(tag).Append(">\n");
            return this;
        }

        /// <summary>Writes a full table from a header row and body rows.</summary>
        public HtmlWriter Table(IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string>> rows, string cssClass = null)
        {
            Open("table", cssClass);
            if (headers != null)
            {
                Open("thead");
                Open("tr");
                foreach (var header in headers)
                {
                    Element("th", header);
                }

                Close("tr");
                Close("thead");
            }

            Open("tbody");
            if (rows != null)
            {
                foreach (var row in rows)
                {
                    Open("tr");
                    foreach (var cell in row)
                    {
                        Element("td", cell);
                    }

                    Close("tr");
                }
            }

            Close("tbody");
            Close("table");
            return this;
        }

        public static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(value.Length + 8);
            foreach (var c in value)
            {
                switch (c)
                {
                    case '&':
                        builder.Append("&amp;");
                        break;
                    case '<':
                        builder.Append("&lt;");
                        break;
                    case '>':
                        builder.Append("&gt;");
                        break;
                    case '"':
                        builder.Append("&quot;");
                        break;
                    case '\'':
                        builder.Append("&#39;");
                        break;
                    default:
                        builder.Append(c);
                        break;
                }
            }

            return builder.ToString();
        }

        private void Indent()
        {
            for (var i = 0; i < _indent; i++)
            {
                _builder.Append("  ");
            }
        }

        public override string ToString()
        {
            return _builder.ToString();
        }
    }
}
