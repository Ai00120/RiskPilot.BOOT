using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace RiskPilot.Diagnostics.Nt8.Core.Report
{
    /// <summary>
    /// Minimal, allocation-conscious JSON writer. The tool intentionally does not depend
    /// on an external serializer so that the emitted schema is explicit, deterministic
    /// and culture-invariant (invariant culture is used for every number).
    /// </summary>
    public sealed class JsonWriter
    {
        private readonly StringBuilder _builder = new StringBuilder();
        private readonly Stack<bool> _hasItems = new Stack<bool>();

        public JsonWriter BeginObject()
        {
            Separate();
            _builder.Append('{');
            _hasItems.Push(false);
            return this;
        }

        /// <summary>Begins a nested object value under the given property name.</summary>
        public JsonWriter BeginObject(string name)
        {
            WriteName(name);
            _builder.Append('{');
            _hasItems.Push(false);
            return this;
        }

        public JsonWriter EndObject()
        {
            _builder.Append('}');
            _hasItems.Pop();
            return this;
        }

        public JsonWriter BeginArray(string name)
        {
            WriteName(name);
            _builder.Append('[');
            _hasItems.Push(false);
            return this;
        }

        public JsonWriter EndArray()
        {
            _builder.Append(']');
            _hasItems.Pop();
            return this;
        }

        public JsonWriter BeginArrayValue()
        {
            Separate();
            _builder.Append('[');
            _hasItems.Push(false);
            return this;
        }

        public JsonWriter WriteProperty(string name, string value)
        {
            WriteName(name);
            WriteString(value);
            return this;
        }

        public JsonWriter WriteProperty(string name, bool value)
        {
            WriteName(name);
            _builder.Append(value ? "true" : "false");
            return this;
        }

        public JsonWriter WriteProperty(string name, int value)
        {
            WriteName(name);
            _builder.Append(value.ToString(CultureInfo.InvariantCulture));
            return this;
        }

        public JsonWriter WriteProperty(string name, long value)
        {
            WriteName(name);
            _builder.Append(value.ToString(CultureInfo.InvariantCulture));
            return this;
        }

        public JsonWriter WriteProperty(string name, double value)
        {
            WriteName(name);
            _builder.Append(value.ToString("R", CultureInfo.InvariantCulture));
            return this;
        }

        public JsonWriter WriteProperty(string name, DateTime value)
        {
            WriteName(name);
            WriteString(value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture));
            return this;
        }

        public JsonWriter WriteProperty(string name, DateTime? value)
        {
            if (value.HasValue)
            {
                WriteProperty(name, value.Value);
            }
            else
            {
                WriteName(name);
                _builder.Append("null");
            }

            return this;
        }

        public JsonWriter WriteProperty(string name, IReadOnlyList<string> values)
        {
            WriteName(name);
            _builder.Append('[');
            if (values != null)
            {
                for (var i = 0; i < values.Count; i++)
                {
                    if (i > 0)
                    {
                        _builder.Append(',');
                    }

                    WriteString(values[i]);
                }
            }

            _builder.Append(']');
            return this;
        }

        public JsonWriter WriteStringArrayProperty(string name, string[] values)
        {
            WriteName(name);
            _builder.Append('[');
            if (values != null)
            {
                for (var i = 0; i < values.Length; i++)
                {
                    if (i > 0)
                    {
                        _builder.Append(',');
                    }

                    WriteString(values[i]);
                }
            }

            _builder.Append(']');
            return this;
        }

        public JsonWriter WriteNull(string name)
        {
            WriteName(name);
            _builder.Append("null");
            return this;
        }

        public JsonWriter WriteRawObject(string name, string rawJson)
        {
            WriteName(name);
            _builder.Append(string.IsNullOrEmpty(rawJson) ? "{}" : rawJson);
            return this;
        }

        private void WriteName(string name)
        {
            Separate();
            WriteString(name);
            _builder.Append(':');
        }

        private void Separate()
        {
            if (_hasItems.Count == 0)
            {
                return;
            }

            if (_hasItems.Peek())
            {
                _builder.Append(',');
            }
            else
            {
                _hasItems.Pop();
                _hasItems.Push(true);
            }
        }

        private void WriteString(string value)
        {
            if (value == null)
            {
                _builder.Append("null");
                return;
            }

            _builder.Append('"');
            foreach (var c in value)
            {
                switch (c)
                {
                    case '"':
                        _builder.Append("\\\"");
                        break;
                    case '\\':
                        _builder.Append("\\\\");
                        break;
                    case '\b':
                        _builder.Append("\\b");
                        break;
                    case '\f':
                        _builder.Append("\\f");
                        break;
                    case '\n':
                        _builder.Append("\\n");
                        break;
                    case '\r':
                        _builder.Append("\\r");
                        break;
                    case '\t':
                        _builder.Append("\\t");
                        break;
                    default:
                        if (c < 0x20)
                        {
                            _builder.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            _builder.Append(c);
                        }

                        break;
                }
            }

            _builder.Append('"');
        }

        public override string ToString()
        {
            return _builder.ToString();
        }
    }
}
